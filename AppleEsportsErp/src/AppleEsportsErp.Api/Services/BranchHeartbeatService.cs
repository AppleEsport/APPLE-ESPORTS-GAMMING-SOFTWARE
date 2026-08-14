using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Sessions;
using AppleEsportsErp.Application.DTOs.Sync;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Configuration;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Tells Head Office what this shop is doing, every thirty seconds.
///
/// The reason this exists rather than another dozen event types: everything anybody wanted to
/// see at Head Office had to be wired by hand into whichever service happened to change it, and
/// the list of what synced became whatever somebody remembered. Sessions were remembered. Bills
/// were remembered. Operator status was not, so a branch trading all evening showed its staff as
/// logged out. PC state was not, so Head Office displayed early August for four days running.
///
/// State is not history and does not want the same machinery. Only the newest beat matters, so
/// there is no queue and no retry: a missed one costs nothing because the next is thirty seconds
/// away. That is what makes it safe to send this often, and why nothing here can lose money.
///
/// Runs on branches only. Head Office is the one being told.
/// </summary>
public class BranchHeartbeatService : BackgroundService
{
    private readonly ILogger<BranchHeartbeatService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Three seconds, so Head Office is watching the shop rather than reading a report about it.
    ///
    /// Thirty was chosen to be frugal and was simply too slow to trust: an owner who starts a
    /// session and stares at an unchanged screen for half a minute concludes sync is broken, and
    /// checking by waiting is no way to run four branches. At three seconds the two screens
    /// agree while you are still looking at them.
    ///
    /// What makes this affordable is on the receiving side, not here: Head Office now writes only
    /// rows whose values actually changed. A quiet shop therefore costs one small row update per
    /// beat no matter how many PCs it has, instead of rewriting every PC twenty times a minute.
    ///
    /// The cost that remains is bandwidth - a few KB each way, so roughly 100 MB a day per
    /// branch. Nothing on a broadband line. Worth knowing if a branch ever runs on a phone
    /// tether for a day.
    /// </summary>
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Logged at most this often when the line is down. Without it a shop offline for a night
    /// writes a warning every thirty seconds — two and a half thousand lines saying the same
    /// thing, burying whatever else happened.
    /// </summary>
    private static readonly TimeSpan ComplainAtMost = TimeSpan.FromMinutes(15);
    private DateTimeOffset _lastComplaint = DateTimeOffset.MinValue;

    public BranchHeartbeatService(
        ILogger<BranchHeartbeatService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
    }

    private static string RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.IsHeadOffice())
        {
            _logger.LogInformation(
                "This instance is Head Office, so it reports its state to nobody. Branches report here.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BeatAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                if (DateTimeOffset.UtcNow - _lastComplaint > ComplainAtMost)
                {
                    _lastComplaint = DateTimeOffset.UtcNow;
                    _logger.LogWarning(
                        "Cannot reach Head Office to report this branch's status ({Reason}). " +
                        "The shop is unaffected; this is only reporting.",
                        ex.GetBaseException().Message);
                }
            }

            await Task.Delay(Every, stoppingToken);
        }
    }

    private async Task BeatAsync(CancellationToken ct)
    {
        var headOffice = _configuration["Sync:HeadOfficeUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(headOffice)) return;

        using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // One branch row, its own, once adopted. Before that there is nothing to report about.
        // Ordered, so a branch whose local database somehow holds more than one row reports
        // the same one every time. Unordered, PostgreSQL is free to return them in any order
        // and the shop could report itself as a different branch between beats.
        var branchId = await db.Branches.AsNoTracking()
            .OrderBy(b => b.Id).Select(b => b.Id).FirstOrDefaultAsync(ct);

        if (branchId == Guid.Empty) return;
        _branchId = branchId;

        var today = IndiaTime.BusinessDayOf(DateTimeOffset.UtcNow);
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRange(today);

        // Who is actually standing at a counter, taken from open shifts rather than from the
        // operator's own status flag. A flag can be left behind by a crash; an open shift is
        // the thing the shop is actually working against.
        var onDuty = await db.Shifts.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status == ShiftStatus.Active)
            .Join(db.Operators.AsNoTracking(), s => s.OperatorId, o => o.Id,
                  (s, o) => new OperatorOnDutyDto
                  {
                      OperatorId = o.Id,
                      FullName = o.FullName,
                      ShiftStartedAt = s.LoginTime,
                  })
            .ToListAsync(ct);

        var pcs = await db.Pcs.AsNoTracking()
            .Where(p => p.BranchId == branchId && !p.IsDeleted)
            .Select(p => new PcStateDto
            {
                PcId = p.Id,
                State = p.State.ToString().ToLowerInvariant(),
                CurrentSessionId = p.CurrentSessionId,
            })
            .ToListAsync(ct);

        var activeSessions = await db.Sessions.AsNoTracking()
            .CountAsync(s => s.BranchId == branchId && s.State == SessionState.Active, ct);

        // Null when nothing is open, and that is not the same as zero: an empty drawer and no
        // drawer at all mean different things to whoever is looking at this.
        var drawer = await db.CashRegisters.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.BusinessDay == today && r.Status != CashRegisterStatus.Closed)
            .OrderByDescending(r => r.OpenedAt)
            .Select(r => (decimal?)r.ExpectedDrawerCash)
            .FirstOrDefaultAsync(ct);

        var takings = await db.Payments.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
            .SumAsync(p => (decimal?)p.TotalAmount, ct) ?? 0m;

        // How far behind sync is. Anything queued and undelivered is money Head Office cannot
        // see yet, so it belongs on the same screen as the takings rather than buried in a log.
        var undelivered = await db.SyncOutboxEntries.AsNoTracking()
            .CountAsync(e => e.SyncedAt == null, ct);

        var beat = new BranchHeartbeatDto
        {
            BranchId = branchId,
            Version = RunningVersion,
            MachineName = Environment.MachineName,
            ConfigVersion = _configVersion,
            BranchLocalTime = IndiaTime.Now,
            OperatorsOnDuty = onDuty,
            Pcs = pcs,
            ActiveSessions = activeSessions,
            DrawerExpected = drawer,
            TakingsToday = takings,
            UndeliveredRecords = undelivered,
        };

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var response = await client.PostAsync(
            $"{headOffice}/api/branch-status",
            new StringContent(JsonSerializer.Serialize(beat), Encoding.UTF8, "application/json"),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            if (DateTimeOffset.UtcNow - _lastComplaint > ComplainAtMost)
            {
                _lastComplaint = DateTimeOffset.UtcNow;
                _logger.LogWarning("Head Office refused this branch's status: {Status}.", response.StatusCode);
            }
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        await ApplyConfigFromReplyAsync(db, body, ct);
        await RunCommandsFromReplyAsync(scope.ServiceProvider, client, headOffice, body, ct);
    }

    /// <summary>
    /// Carries out whatever Head Office has asked this branch to do, and reports back.
    ///
    /// This is the other missing direction, and it is deliberately narrow: a command is never
    /// allowed to write a session, a PC, or any other fact directly. It calls the exact same
    /// service method an operator's own click calls - ISessionService.StopSessionAsync - so a
    /// remote stop is billed, logged and synced upward exactly like a local one. Head Office
    /// asking and Head Office writing are not the same thing, and only the first is safe: the
    /// second is what put an unbillable ₹60 session on ADJ-PC-01 in the first place.
    /// </summary>
    private async Task RunCommandsFromReplyAsync(
        IServiceProvider scoped, HttpClient client, string headOffice, string body, CancellationToken ct)
    {
        List<BranchCommandDto>? commands;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("commands", out var node) || node.ValueKind != JsonValueKind.Array) return;

            commands = JsonSerializer.Deserialize<List<BranchCommandDto>>(node.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read the instructions Head Office sent ({Reason}).",
                ex.GetBaseException().Message);
            return;
        }

        if (commands is null || commands.Count == 0) return;

        foreach (var command in commands)
        {
            var (succeeded, message) = await RunOneCommandAsync(scoped, command, ct);

            _logger.LogInformation("Command {Type} ({Id}) from Head Office: {Result} - {Message}",
                command.CommandType, command.Id, succeeded ? "done" : "failed", message);

            try
            {
                var result = new BranchCommandResultDto
                {
                    CommandId = command.Id,
                    Succeeded = succeeded,
                    Message = message,
                };

                await client.PostAsync(
                    $"{headOffice}/api/branch-status/commands/result",
                    new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json"),
                    ct);
            }
            catch (Exception ex)
            {
                // The command still ran (or was refused) - only the confirmation was lost. Head
                // Office will hand the same command back on the next beat, this branch will find
                // the session already stopped, and will confirm it then. Nothing to do here but
                // let that happen.
                _logger.LogWarning("Ran command {Id} but could not confirm it to Head Office ({Reason}).",
                    command.Id, ex.GetBaseException().Message);
            }
        }
    }

    /// <summary>
    /// One command, dispatched to whatever actually knows how to do it.
    ///
    /// An unrecognised CommandType fails cleanly rather than throwing - a branch on an older
    /// build meeting a command type invented after it shipped should say so plainly, not crash
    /// the beat that everything else here depends on.
    /// </summary>
    private static async Task<(bool succeeded, string message)> RunOneCommandAsync(
        IServiceProvider scoped, BranchCommandDto command, CancellationToken ct)
    {
        switch (command.CommandType)
        {
            case BranchCommands.StopSession:
                return await RunStopSessionAsync(scoped, command.Payload, ct);

            case BranchCommands.StartSession:
                return await RunStartSessionAsync(scoped, command.Payload, ct);

            case BranchCommands.SetPcState:
                return await RunSetPcStateAsync(scoped, command.Payload, ct);

            default:
                return (false, $"This branch does not know the command '{command.CommandType}' yet.");
        }
    }

    /// <summary>
    /// Starts play on one of this branch's PCs because Head Office asked.
    ///
    /// Every check an operator's own Start would hit still applies - PC free, pricing profile
    /// present, member's wallet funded, member not already playing elsewhere - because this is
    /// literally the same method. A remote start that skipped them would be the old bug wearing
    /// a different hat: a session the counter cannot account for.
    ///
    /// The operator recorded is whoever is on shift here right now, since the session's takings
    /// belong in that person's shift and till. If nobody is on shift the branch refuses, and
    /// says so - money taken outside a shift has nowhere to be counted at End of Day.
    /// </summary>
    private static async Task<(bool, string)> RunStartSessionAsync(
        IServiceProvider scoped, string payload, CancellationToken ct)
    {
        Guid pcId;
        SessionStartDto dto;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            pcId = root.GetProperty("pcId").GetGuid();

            dto = new SessionStartDto
            {
                PcId = pcId,
                MemberId = root.TryGetProperty("memberId", out var m) && m.ValueKind != JsonValueKind.Null
                    ? m.GetGuid() : null,
                CustomerName = root.TryGetProperty("customerName", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null,
                DurationMinutes = root.TryGetProperty("durationMinutes", out var d) && d.ValueKind == JsonValueKind.Number
                    ? d.GetDecimal() : 0m,
                PackageName = root.TryGetProperty("packageName", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString() ?? "Head Office" : "Head Office",
                ExpectedAmount = root.TryGetProperty("expectedAmount", out var e) && e.ValueKind == JsonValueKind.Number
                    ? e.GetDecimal() : 0m,
                Notes = root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : null,
            };
        }
        catch
        {
            return (false, "The start command arrived without a readable PC and duration.");
        }

        var db = scoped.GetRequiredService<AppDbContext>();

        var pc = await db.Pcs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pcId, ct);
        if (pc is null) return (false, "No such PC exists at this branch.");

        var shift = await db.Shifts.AsNoTracking()
            .Where(s => s.BranchId == pc.BranchId && s.Status == ShiftStatus.Active)
            .OrderByDescending(s => s.LoginTime)
            .FirstOrDefaultAsync(ct);

        if (shift is null)
            return (false,
                "Nobody is on shift at this branch, so there is no till to bill this into. " +
                "Start a shift at the counter first.");

        try
        {
            var sessionService = scoped.GetRequiredService<ISessionService>();
            var result = await sessionService.StartSessionAsync(pc.BranchId, shift.OperatorId, shift.Id, dto);

            return (true, $"Started on {result.PcName} for {result.DurationMinutes} min, Rs {result.ExpectedAmount}.");
        }
        catch (Exception ex)
        {
            return (false, ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Takes a PC out of service, or puts it back, because Head Office asked.
    ///
    /// Refuses while somebody is playing on it. Flipping a busy machine to maintenance would
    /// leave a live session attached to a PC that no longer admits to having one, and the
    /// operator would be unable to bill it - the exact shape of the original problem. Stop the
    /// session first; that is a separate command and Head Office can send both.
    /// </summary>
    private static async Task<(bool, string)> RunSetPcStateAsync(
        IServiceProvider scoped, string payload, CancellationToken ct)
    {
        Guid pcId;
        string wanted;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            pcId = doc.RootElement.GetProperty("pcId").GetGuid();
            wanted = doc.RootElement.GetProperty("state").GetString() ?? string.Empty;
        }
        catch
        {
            return (false, "The PC state command arrived without a readable PC and state.");
        }

        if (!Enum.TryParse<PcState>(wanted.Replace("_", string.Empty), ignoreCase: true, out var state))
            return (false, $"This branch does not recognise the PC state '{wanted}'.");

        var db = scoped.GetRequiredService<AppDbContext>();
        var pc = await db.Pcs.FirstOrDefaultAsync(p => p.Id == pcId, ct);
        if (pc is null) return (false, "No such PC exists at this branch.");

        if (pc.State == state)
            return (true, $"Already {state.ToString().ToLowerInvariant()} - nothing to do.");

        if (pc.State is PcState.Active or PcState.AwaitingBilling)
            return (false,
                $"{pc.PcNumber} is {pc.State.ToString().ToLowerInvariant()} - stop the session first, " +
                "or the bill for it could not be collected.");

        pc.State = state;
        pc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return (true, $"{pc.PcNumber} is now {state.ToString().ToLowerInvariant()}.");
    }

    private static async Task<(bool, string)> RunStopSessionAsync(
        IServiceProvider scoped, string payload, CancellationToken ct)
    {
        Guid sessionId;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            sessionId = doc.RootElement.GetProperty("sessionId").GetGuid();
        }
        catch
        {
            return (false, "The stop command arrived without a readable session id.");
        }

        var db = scoped.GetRequiredService<AppDbContext>();
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
            return (false, "No such session exists at this branch.");

        // Genuinely nothing left to do - not a failure. The operator may have stopped it
        // themselves in the time the command took to arrive, and that is the correct outcome
        // either way: the session is stopped, which is all Head Office actually asked for.
        if (session.State != SessionState.Active)
            return (true, $"Already {session.State.ToString().ToLowerInvariant()} - nothing to do.");

        try
        {
            var sessionService = scoped.GetRequiredService<ISessionService>();

            // deferPayment is NOT "skip billing for a remote stop" - it marks the bill paid on
            // the spot and writes the balance off as a CustomerCredit, as if the customer had
            // walked out owing money. A remote stop must never quietly forgive a debt nobody
            // has actually decided to forgive. False is the same outcome an operator's own
            // Stop button gives: if nothing is owed the PC frees immediately; if something is,
            // it goes to Awaiting Billing exactly as if stopped at the counter, and whoever is
            // standing there collects it normally.
            //
            // The operator who started the session is recorded as the one who stopped it,
            // because they are the one whose shift and till this affects - the same as if they
            // had pressed Stop themselves.
            var result = await sessionService.StopSessionAsync(
                session.BranchId, session.OperatorId, sessionId, deferPayment: false);

            return (true, $"Stopped. {result.PackageName}, billed {result.DurationMinutes} min, Rs {result.ExpectedAmount}.");
        }
        catch (Exception ex)
        {
            return (false, ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Takes whatever settings Head Office sent back and makes this branch match them.
    ///
    /// This is the direction that never existed. A super admin could grant an operator End of
    /// Day, watch the server save it, and the counter would never hear - every permission
    /// screen on the server was decoration, and an operator hired at Head Office could not log
    /// in at the shop they had been hired for.
    ///
    /// Head Office sends nothing at all when this branch is already correct, so the usual case
    /// is a few hundred bytes and this method does nothing.
    /// </summary>
    private async Task ApplyConfigFromReplyAsync(AppDbContext db, string body, CancellationToken ct)
    {
        BranchConfigDto? config;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("config", out var node) || node.ValueKind == JsonValueKind.Null) return;

            config = JsonSerializer.Deserialize<BranchConfigDto>(node.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read the settings Head Office sent ({Reason}).",
                ex.GetBaseException().Message);
            return;
        }

        if (config is null) return;
        if (config.Operators.Count == 0 && config.MenuItems.Count == 0 && config.Members.Count == 0) return;

        var known = await db.Operators.ToDictionaryAsync(o => o.Id, ct);
        var changed = 0;

        foreach (var incoming in config.Operators)
        {
            if (!known.TryGetValue(incoming.Id, out var op))
            {
                // Somebody hired at Head Office who has never existed here. Created with the
                // same id, so everything they later do lines up on both sides rather than
                // arriving as a stranger.
                op = new Operator
                {
                    Id = incoming.Id,
                    BranchId = _branchId,
                    Status = OperatorStatus.LoggedOut,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Operators.Add(op);
                changed++;
            }

            op.FullName = incoming.FullName;
            op.Username = incoming.Username;
            op.Email = incoming.Email;
            op.PasswordHash = incoming.PasswordHash;
            op.MobileNumber = incoming.MobileNumber;
            op.AccessPin = incoming.AccessPin;
            op.IsGlobalAdmin = incoming.IsGlobalAdmin;
            op.DashboardPermissions = incoming.DashboardPermissions;
            op.UpdatedAt = DateTimeOffset.UtcNow;

            // Only the barred/not-barred decision comes down. Active and LoggedOut say whether
            // somebody is standing at this counter, which Head Office cannot know and must
            // never overwrite - doing so would sign out the operator halfway through a shift.
            if (incoming.IsBlocked)
            {
                if (op.Status is not (OperatorStatus.Suspended or OperatorStatus.Disabled))
                    op.Status = OperatorStatus.Suspended;
            }
            else if (op.Status is OperatorStatus.Suspended or OperatorStatus.Disabled)
            {
                op.Status = OperatorStatus.LoggedOut;   // unbarred; duty is decided here
            }
        }

        var menuChanged = await ApplyMenuItemsAsync(db, _branchId, config.MenuItems, ct);
        var membersChanged = await ApplyMembersAsync(db, config.Members, ct);

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);

            // Recorded once, not every beat: the fingerprint now matches so Head Office stops
            // sending it. Worth a line in the log, because "the shop suddenly behaves
            // differently" should always have something to point at.
            _logger.LogInformation(
                "Settings updated from Head Office: {OpCount} operator(s), {OpNew} new; " +
                "{MenuCount} menu item(s), {MenuNew} new; {MemberCount} member(s), {MemberNew} new. " +
                "Version {Version}.",
                config.Operators.Count, changed,
                config.MenuItems.Count, menuChanged,
                config.Members.Count, membersChanged,
                config.Version);
        }

        _configVersion = config.Version;
    }

    /// <summary>
    /// Makes this branch's menu match Head Office's catalog for it.
    ///
    /// This is the fix for a super admin adding a food item at Head Office and it never
    /// appearing at the counter - the Menu Editor is branch-scoped storage, and an item added
    /// "for Adajan" while working at Head Office was written into Head Office's own copy of
    /// Adajan's table, which the physical Adajan counter - a completely separate database -
    /// was never told about.
    ///
    /// Only catalog fields are touched: name, category, price, image, whether Head Office has
    /// withdrawn it from sale. CurrentStock and SoldQty are never written here, for the same
    /// reason a PC's busy/idle state is never written from config - they are this branch's own
    /// trading state and change at the counter, not at Head Office. A shop that just sold its
    /// last plate of fries must not have Head Office silently restock it on the next beat.
    /// </summary>
    private static async Task<int> ApplyMenuItemsAsync(
        AppDbContext db, Guid branchId, List<BranchMenuItemConfigDto> incoming, CancellationToken ct)
    {
        if (incoming.Count == 0) return 0;

        var known = await db.Set<InventoryItem>().ToDictionaryAsync(i => i.Id, ct);
        var added = 0;

        foreach (var item in incoming)
        {
            if (!known.TryGetValue(item.Id, out var row))
            {
                row = new InventoryItem
                {
                    Id = item.Id,
                    BranchId = branchId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Add(row);
                added++;
            }

            // Compared before writing, and this matters more than it looks now that the menu
            // also travels upward. Assigning the same value still marks the row Modified, which
            // SyncCapture would faithfully record as a change and send back to Head Office -
            // every item, every time a config arrived, describing nothing that had happened.
            var differs =
                row.ItemName != item.ItemName
                || row.Category != item.Category
                || row.Price != item.Price
                || row.ImageUrl != item.ImageUrl;

            if (differs)
            {
                row.ItemName = item.ItemName;
                row.Category = item.Category;
                row.Price = item.Price;
                row.ImageUrl = item.ImageUrl;
            }

            // A branch marking something Out of Stock is its own call and stays exactly as it
            // is; only Head Office's Disabled/not-Disabled decision moves this needle, and only
            // when it actually says something - "disabled" pulls an item from sale everywhere,
            // "not disabled" must not silently un-hide something the branch itself paused.
            if (item.IsDisabled && row.Status != FoodAvailability.Disabled)
            {
                row.Status = FoodAvailability.Disabled;
                differs = true;
            }
            else if (!item.IsDisabled && row.Status == FoodAvailability.Disabled)
            {
                row.Status = FoodAvailability.Available;
                differs = true;
            }

            if (differs) row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return added;
    }

    /// <summary>
    /// Makes this branch recognise every member Head Office knows about, with the wallet
    /// balance Head Office currently holds for them.
    ///
    /// This is what lets someone who joined at Adajan spend their wallet at Katargam: without
    /// it, a branch that has never seen a member locally has no row for them at all. Balance is
    /// only ever accepted from Head Office when it is not older than what this branch already
    /// has - the same "newest wins" rule used for a PC's state, applied to money instead. A
    /// branch that just took a top-up of its own must not have that top-up erased because Head
    /// Office's reply, built moments earlier, has not caught up yet.
    /// </summary>
    private static async Task<int> ApplyMembersAsync(
        AppDbContext db, List<BranchMemberConfigDto> incoming, CancellationToken ct)
    {
        if (incoming.Count == 0) return 0;

        var known = await db.Members.ToDictionaryAsync(m => m.Id, ct);
        var added = 0;

        foreach (var item in incoming)
        {
            if (!known.TryGetValue(item.Id, out var member))
            {
                member = new Member
                {
                    Id = item.Id,
                    GamingBalance = item.GamingBalance,
                    FoodBalance = item.FoodBalance,
                    BalanceAsOf = item.BalanceAsOf,
                    Status = item.IsBlocked ? MemberStatus.Suspended : MemberStatus.Active,
                    JoinDate = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Members.Add(member);
                added++;
            }
            else if (item.BalanceAsOf is { } incomingAsOf
                     && (member.BalanceAsOf is not { } localAsOf || incomingAsOf > localAsOf))
            {
                member.GamingBalance = item.GamingBalance;
                member.FoodBalance = item.FoodBalance;
                member.BalanceAsOf = incomingAsOf;
            }

            member.FullName = item.FullName;
            member.MemberNumber = item.MemberNumber;
            member.MobileNumber = item.MobileNumber;
            member.Email = item.Email;
            member.Username = item.Username;
            member.UpdatedAt = DateTimeOffset.UtcNow;

            // Same rule as operators: only the barred decision comes down. There is no local
            // "on shift" equivalent for a member to protect, but Active/Vip is still this
            // branch's own read on a member's standing and is left alone either way.
            if (item.IsBlocked && member.Status is not MemberStatus.Suspended)
                member.Status = MemberStatus.Suspended;
            else if (!item.IsBlocked && member.Status is MemberStatus.Suspended)
                member.Status = MemberStatus.Active;
        }

        return added;
    }

    /// <summary>
    /// The settings fingerprint this branch is running on, sent with every beat so Head Office
    /// can stay silent while it matches. Held in memory only - after a restart the branch
    /// simply asks once more and is told once more, which costs one message.
    /// </summary>
    private string? _configVersion;

    /// <summary>This branch's own id, remembered from the beat that was just built.</summary>
    private Guid _branchId;
}
