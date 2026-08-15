using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Billing;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Configuration;

namespace AppleEsportsErp.Infrastructure.Services;

public class BillingService : IBillingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly IHubNotificationService _hubNotification;
    private readonly IWalletService _walletService;
    private readonly IOutboxService _outbox;
    private readonly IConfiguration _configuration;

    public BillingService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        IHubNotificationService hubNotification,
        IWalletService walletService,
        IOutboxService outbox,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _hubNotification = hubNotification;
        _walletService = walletService;
        _outbox = outbox;
        _configuration = configuration;
    }

    /// <summary>
    /// A payment taken here and nowhere else. Head Office holds a synced copy of every branch's
    /// bills, and writing "paid" into that copy is easy and looks correct immediately - the
    /// screen updates, the audit log gets an entry - but the branch that actually has the
    /// customer, the till and the cash was never told. Its own register stays uncredited and its
    /// PC stays locked showing Billing, forever, because nothing there ever changed. Same reason
    /// SessionService refuses to start/stop a session directly at Head Office: see
    /// BillingController.ProcessPayment for the branch-command instruction this sends instead.
    /// </summary>
    private void RefuseIfHeadOffice(string what)
    {
        if (!_configuration.IsHeadOffice()) return;

        throw new AppException(
            $"This bill has to be {what} by the branch itself, not written here at Head Office - " +
            "a payment written here is invisible to the counter, so the register is never credited " +
            "and the PC never frees up. Send it as an instruction instead (api/bills/{id}/pay) and " +
            "the branch will carry it out within a few seconds and report back.",
            System.Net.HttpStatusCode.BadRequest,
            "BRANCH_ONLY_OPERATION");
    }

    public async Task<PaginatedResult<BillDto>> GetActiveBillsAsync(Guid branchId, int page = 1, int pageSize = 50)
    {
        var query = _unitOfWork.Repository<Bill>().Query()
            .Include(b => b.Items)
            .Include(b => b.Payments)
            .Include(b => b.Pc)
            .Where(b => b.BranchId == branchId && b.Status != BillStatus.Completed)
            .OrderByDescending(b => b.CreatedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = items.Select(MapToDto).ToList();
        return new PaginatedResult<BillDto>(dtos, total, page, pageSize);
    }

    public async Task<List<BillDto>> GetDeferredBillsAsync(Guid branchId)
    {
        var bills = await _unitOfWork.Repository<Bill>().Query()
            .Include(b => b.Items)
            .Include(b => b.Payments)
            .Include(b => b.Pc)
            .Include(b => b.Session)
            .Where(b => b.BranchId == branchId && b.IsDeferred && b.Status == BillStatus.Pending)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return bills.Select(b => MapToDto(b)).ToList();
    }

    public async Task<BillDto> GetBillAsync(Guid branchId, Guid id)
    {
        var bill = await _unitOfWork.Repository<Bill>().Query()
            .Include(b => b.Items)
            .Include(b => b.Payments)
            .Include(b => b.Pc).ThenInclude(p => p!.PricingProfile)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == id && b.BranchId == branchId)
            ?? throw new NotFoundException("Bill not found.");

        return MapToDtoWithLiveAmount(bill);
    }

    public async Task<BillDto> GetBillByNumberAsync(Guid branchId, string billNumber)
    {
        var bill = await _unitOfWork.Repository<Bill>().Query()
            .Include(b => b.Items)
            .Include(b => b.Payments)
            .Include(b => b.Pc).ThenInclude(p => p!.PricingProfile)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.BillNumber == billNumber && b.BranchId == branchId)
            ?? throw new NotFoundException("Bill not found.");

        return MapToDtoWithLiveAmount(bill);
    }

    /// <summary>
    /// Same mapping as MapToDto, except while the session is still Active it recomputes the
    /// gaming charge live (same formula as the operator PC card / member overlay) instead of
    /// returning the stale amount stored at session start — this is what keeps the Billing
    /// Counter's bill panel from showing a different number than everywhere else.
    /// </summary>
    private static BillDto MapToDtoWithLiveAmount(Bill bill)
    {
        var dto = MapToDto(bill);

        if (bill.Session != null && bill.Session.State == Domain.Enums.SessionState.Active)
        {
            decimal ratePerHour = bill.Pc?.PricingProfile?.BaseHourlyRate ?? Application.Services.SessionPricingCalculator.DefaultRatePerHour;
            int bufferMinutes = bill.Pc?.PricingProfile?.BufferMinutes ?? Application.Services.SessionPricingCalculator.DefaultBufferMinutes;
            decimal elapsedMinutes = (decimal)(DateTimeOffset.UtcNow - bill.Session.StartTime).TotalMinutes;
            decimal liveGamingAmount = Application.Services.SessionPricingCalculator.CalculateGamingAmount(ratePerHour, bufferMinutes, elapsedMinutes);

            var (displayGaming, displayFood, roundedTotal) = Application.Services.SessionPricingCalculator.ComputeRoundedBreakdown(
                liveGamingAmount, dto.FoodAmount, dto.DiscountAmount);

            dto.GamingAmount = displayGaming;
            dto.Subtotal = displayGaming + displayFood;
            dto.TotalAmount = roundedTotal;

            var gamingItem = dto.Items.FirstOrDefault(i => i.ItemType == "gaming");
            if (gamingItem != null)
            {
                gamingItem.UnitPrice = displayGaming;
                gamingItem.TotalPrice = displayGaming;
            }
        }

        return dto;
    }

    public async Task<BillDto> ApplyDiscountAsync(Guid branchId, Guid actorId, string actorRole, Guid id, ApplyDiscountDto dto)
    {
        RefuseIfHeadOffice("discounted");

        var bill = await _unitOfWork.Repository<Bill>().Query()
            .Include(b => b.Items)
            .Include(b => b.Payments)
            .Include(b => b.Pc).ThenInclude(p => p!.PricingProfile)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == id && b.BranchId == branchId)
            ?? throw new NotFoundException("Bill not found.");

        if (bill.Status == BillStatus.Completed)
            throw new AppException("Cannot apply discount to a completed bill.");

        // What the gaming line is worth RIGHT NOW, before any discount.
        //
        // Two bugs lived in reading bill.GamingAmount/bill.Subtotal directly here.
        //
        // The stored figure is written once at session start as ExpectedAmount, which for an
        // open/PAYG session is 0 - so a percentage discount computed 0% of 0, saved happily,
        // and returned 200 OK having done nothing. That is the "button applies no value at
        // all" report. Every screen meanwhile shows the live figure via
        // MapToDtoWithLiveAmount, so the number the admin was discounting was never the
        // number they could see.
        //
        // And because the block below writes the rounding-adjusted gaming back onto the bill,
        // a second press discounted an already-discounted base and folded another rounding
        // delta in. Pressing 10% twice did not mean 10%.
        //
        // Recomputing from elapsed time each call fixes both: the base is always the true
        // pre-discount charge, so the discount is never compounded and never applied to zero.
        decimal baseGaming = bill.GamingAmount;
        if (bill.Session != null && bill.Session.State == Domain.Enums.SessionState.Active)
        {
            decimal ratePerHour = bill.Pc?.PricingProfile?.BaseHourlyRate
                ?? Application.Services.SessionPricingCalculator.DefaultRatePerHour;
            int bufferMinutes = bill.Pc?.PricingProfile?.BufferMinutes
                ?? Application.Services.SessionPricingCalculator.DefaultBufferMinutes;
            decimal elapsedMinutes = (decimal)(DateTimeOffset.UtcNow - bill.Session.StartTime).TotalMinutes;
            baseGaming = Application.Services.SessionPricingCalculator.CalculateGamingAmount(
                ratePerHour, bufferMinutes, elapsedMinutes);
        }

        decimal baseSubtotal = baseGaming + bill.FoodAmount;

        decimal discountAmount = 0;
        if (dto.DiscountType == DiscountType.Percentage)
        {
            discountAmount = baseSubtotal * (dto.DiscountValue / 100);
        }
        else if (dto.DiscountType == DiscountType.Flat)
        {
            discountAmount = dto.DiscountValue;
        }

        if (discountAmount > baseSubtotal)
            throw new AppException("Discount amount cannot exceed bill subtotal.");

        // Discount is an explicit, deliberate figure the admin chose — keep it exact.
        // The Gaming line (derived, not a fixed price) absorbs any rounding instead.
        var (displayGaming, displayFood, roundedTotal) = Application.Services.SessionPricingCalculator.ComputeRoundedBreakdown(
            baseGaming, bill.FoodAmount, discountAmount);

        bill.DiscountType = dto.DiscountType;
        bill.DiscountValue = dto.DiscountValue;
        bill.DiscountAmount = discountAmount;
        bill.DiscountBy = actorId;
        bill.DiscountReason = dto.Reason;
        bill.GamingAmount = displayGaming;
        bill.FoodAmount = displayFood;
        bill.Subtotal = displayGaming + displayFood;
        bill.TotalAmount = roundedTotal;
        bill.UpdatedAt = DateTimeOffset.UtcNow;

        var discountGamingItem = bill.Items.FirstOrDefault(i => i.ItemType == "gaming");
        if (discountGamingItem != null)
        {
            discountGamingItem.TotalPrice = displayGaming;
            discountGamingItem.UnitPrice = displayGaming;
        }

        _unitOfWork.Repository<Bill>().Update(bill);

        await _auditService.LogAsync(new AuditEntry
        {
            // Whoever actually pressed it. This said UserName = "System" and hardcoded the role
            // to SuperAdmin, so the audit trail recorded every discount as having been applied
            // by nobody in particular - on the one action in this system most likely to be
            // questioned later. The id was always here; only the name and role were invented.
            // Both, because the actor may live in either table - a branch Admin is an
            // operator row, a Super Admin is a user row - and AuditService resolves the
            // real name from whichever one matches.
            OperatorId = actorId,
            UserId = actorId,
            UserRole = actorRole,
            UserName = string.Empty,
            Action = AuditActions.DiscountApply,
            BranchId = branchId,
            TargetType = "bill",
            TargetId = bill.Id,
            Details = new
            {
                DiscountType = dto.DiscountType.ToString(),
                Value = dto.DiscountValue,
                Reason = dto.Reason,
                // The figures it was actually computed against, so the amount can be checked
                // afterwards rather than taken on trust.
                BaseSubtotal = baseSubtotal,
                DiscountAmount = discountAmount,
                NewTotal = roundedTotal,
            }
        });

        await _unitOfWork.SaveChangesAsync();
        await _hubNotification.BroadcastBillingUpdateAsync(branchId, bill.Id);

        return MapToDto(bill);
    }

    public async Task<BillDto> ProcessPaymentAsync(Guid branchId, Guid operatorId, Guid shiftId, Guid id, ProcessPaymentDto dto)
    {
        RefuseIfHeadOffice("paid");

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var bill = await _unitOfWork.Repository<Bill>().Query()
                .Include(b => b.Items)
                .Include(b => b.Payments)
                .Include(b => b.Pc)
                .Include(b => b.Session)
                .Include(b => b.Member)
                .FirstOrDefaultAsync(b => b.Id == id && b.BranchId == branchId)
                ?? throw new NotFoundException("Bill not found.");

            if (bill.Status == BillStatus.Completed)
                throw new AppException("Bill is already completed.");

            // Link member if passed in payment dto and not already linked
            if (dto.MemberId.HasValue && bill.MemberId == null)
            {
                bill.MemberId = dto.MemberId.Value;
                if (bill.SessionId.HasValue)
                {
                    var session = await _unitOfWork.Repository<Session>().GetByIdAsync(bill.SessionId.Value);
                    if (session != null)
                    {
                        session.MemberId = dto.MemberId.Value;
                        _unitOfWork.Repository<Session>().Update(session);
                    }
                }
            }

            // No leg of a payment may be negative, and this is where the negative figures came
            // from.
            //
            // The only check was that the parts add up to the bill, which a negative leg passes
            // effortlessly: Rs 500 cash and minus Rs 400 online sums to Rs 100 and settles a
            // Rs 100 bill. Nothing else objected, because nothing else looked. Rs 500 then went
            // into the till's expected cash while the takings recorded Rs 100, so the drawer
            // over-counted by Rs 400 at End of Day; the online column went negative; and the
            // session it belonged to reported a negative total in the reports.
            //
            // There is no validator on this DTO at all - only Auth and Sessions have any - so
            // the guard belongs here in the service, where every route in (counter, member
            // checkout, overlay) has to pass through it, rather than in one controller.
            //
            // Money moving the other way is a refund, which is a different operation with
            // different rules about who may authorise it. It is not a payment with a minus sign.
            if (dto.CashAmount < 0 || dto.OnlineAmount < 0 || dto.WalletAmount < 0
                || dto.CreditAmount < 0 || dto.CashReceived < 0)
            {
                throw new AppException(
                    "A payment cannot contain a negative amount. Cash, online, wallet, credit and " +
                    "cash received must each be zero or more.",
                    System.Net.HttpStatusCode.BadRequest,
                    "NEGATIVE_PAYMENT_AMOUNT");
            }

            // Calculate total paid vs expected
            decimal totalPayment = dto.CashAmount + dto.OnlineAmount + dto.WalletAmount;
            if (totalPayment + dto.CreditAmount != bill.TotalAmount)
                throw new AppException($"Payment amount mismatch. Expected: {bill.TotalAmount}, Provided: {totalPayment} + Credit: {dto.CreditAmount}");

            decimal changeReturned = 0;
            decimal actualCashCollected = dto.CashAmount;
            if (dto.CashAmount > 0)
            {
                if (dto.CashReceived < dto.CashAmount)
                    throw new AppException("Cash received is less than cash amount to be paid.");
                changeReturned = dto.CashReceived - dto.CashAmount;
                actualCashCollected = dto.CashAmount;
            }

            // Perform Wallet Deduction first if Wallet payment is involved
            if (dto.WalletAmount > 0)
            {
                if (bill.MemberId == null)
                    throw new AppException("Cannot pay via Wallet for a walk-in customer. Member registration required.");
                    
                decimal totalBill = bill.Subtotal > 0 ? bill.Subtotal : 1;
                decimal gamingDeduction = dto.WalletAmount * (bill.GamingAmount / totalBill);
                decimal foodDeduction = dto.WalletAmount * (bill.FoodAmount / totalBill);

                if (gamingDeduction > 0)
                {
                    await _walletService.DeductWalletAsync(branchId, operatorId, bill.ShiftId, bill.MemberId.Value, new Application.DTOs.Wallets.DeductWalletDto
                    {
                        TargetWallet = WalletType.Gaming,
                        Amount = gamingDeduction,
                        Reason = $"Gaming Payment for Bill {bill.BillNumber}",
                        BillId = bill.Id
                    });
                }
                
                if (foodDeduction > 0)
                {
                    await _walletService.DeductWalletAsync(branchId, operatorId, bill.ShiftId, bill.MemberId.Value, new Application.DTOs.Wallets.DeductWalletDto
                    {
                        TargetWallet = WalletType.Food,
                        Amount = foodDeduction,
                        Reason = $"Food Payment for Bill {bill.BillNumber}",
                        BillId = bill.Id
                    });
                }
            }

            // Process Payment Record
            var payment = new Payment
            {
                BillId = bill.Id,
                BranchId = branchId,
                OperatorId = operatorId,
                PaymentType = dto.PaymentType,
                TotalAmount = totalPayment,
                CashAmount = dto.CashAmount,
                OnlineAmount = dto.OnlineAmount,
                WalletAmount = dto.WalletAmount,
                CashReceived = dto.CashReceived,
                ChangeReturned = changeReturned,
                ActualCashCollected = actualCashCollected,
                GamingPortion = bill.GamingAmount - (bill.DiscountAmount * (bill.GamingAmount / (bill.Subtotal > 0 ? bill.Subtotal : 1))), // Prorate discount
                FoodPortion = bill.FoodAmount - (bill.DiscountAmount * (bill.FoodAmount / (bill.Subtotal > 0 ? bill.Subtotal : 1))),
                Status = "completed",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _unitOfWork.Repository<Payment>().AddAsync(payment);

            if (dto.CreditAmount > 0)
            {
                var customerCredit = new CustomerCredit
                {
                    BranchId = branchId,
                    OperatorId = operatorId,
                    BillId = bill.Id,
                    CustomerName = !string.IsNullOrWhiteSpace(dto.CustomerName) ? dto.CustomerName : (bill.CustomerName ?? bill.Member?.Username ?? "Walk-in"),
                    CustomerPhone = dto.CustomerPhone ?? "N/A",
                    PcNumber = bill.Pc?.PcNumber ?? "N/A",
                    OriginalBillAmount = bill.TotalAmount,
                    AmountPaidInitially = totalPayment,
                    CreditAmount = dto.CreditAmount,
                    Status = "pending",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await _unitOfWork.Repository<CustomerCredit>().AddAsync(customerCredit);
            }

            // Update Bill
            bill.PaymentType = dto.PaymentType;
            bill.CashAmount = dto.CashAmount;
            bill.OnlineAmount = dto.OnlineAmount;
            bill.WalletAmount = dto.WalletAmount;
            bill.CashReceived = dto.CashReceived;
            bill.ChangeReturned = changeReturned;
            bill.ActualCashCollected = actualCashCollected;
            bill.Status = BillStatus.Completed;
            bill.CompletedAt = DateTimeOffset.UtcNow;
            bill.IsDeferred = false;
            bill.UpdatedAt = DateTimeOffset.UtcNow;
            
            _unitOfWork.Repository<Bill>().Update(bill);

            // Cash Register Tracking (SOP §10.2)
            if (dto.CashAmount > 0)
            {
                var activeRegister = await _unitOfWork.Repository<CashRegister>().Query()
                    .FirstOrDefaultAsync(cr => cr.BranchId == branchId && cr.ShiftId == shiftId && cr.Status == CashRegisterStatus.Open)
                    ?? throw new AppException("No active cash register found for this shift.");

                activeRegister.ExpectedDrawerCash += actualCashCollected;
                activeRegister.TotalCashSales += actualCashCollected;
                _unitOfWork.Repository<CashRegister>().Update(activeRegister);

                var cashTx = new CashTransaction
                {
                    CashRegisterId = activeRegister.Id,
                    BillId = bill.Id,
                    BranchId = branchId,
                    OperatorId = operatorId,
                    PcNumber = bill.Pc?.PcNumber,
                    CustomerName = !string.IsNullOrWhiteSpace(dto.CustomerName) ? dto.CustomerName : (bill.CustomerName ?? bill.Member?.Username ?? "Walk-in"),
                    TransactionType = "billing",
                    CashAmount = dto.CashAmount,
                    CashReceived = dto.CashReceived,
                    ChangeReturned = changeReturned,
                    ActualCashCollected = actualCashCollected,
                    GamingAmount = payment.GamingPortion * (dto.CashAmount / totalPayment), // Prorate cash to gaming
                    FoodAmount = payment.FoodPortion * (dto.CashAmount / totalPayment),     // Prorate cash to food
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await _unitOfWork.Repository<CashTransaction>().AddAsync(cashTx);
            }

            Guid? completedSessionId = null;
            Guid? releasedPcId = null;

            // Release PC & Session (SOP §9.2)
            if (bill.Pc != null)
            {
                var pc = bill.Pc;
                
                // If there's an active session, stop it automatically upon payment
                if (bill.SessionId.HasValue)
                {
                    var session = await _unitOfWork.Repository<Session>().GetByIdAsync(bill.SessionId.Value);
                    if (session != null && session.State == SessionState.Active)
                    {
                        var now = DateTimeOffset.UtcNow;
                        session.State = SessionState.Completed;
                        session.UpdatedAt = now;
                        session.EndTime = now;
                        session.ActualDurationMin = (int)(now - session.StartTime).TotalMinutes;
                        _unitOfWork.Repository<Session>().Update(session);
                        completedSessionId = session.Id;
                    }
                }

                // If PC is AwaitingBilling or Active, we release it back to Idle
                if (pc.State == PcState.AwaitingBilling || pc.State == PcState.Active)
                {
                    pc.State = PcState.Idle;
                    pc.CurrentSessionId = null;
                    _unitOfWork.Repository<Pc>().Update(pc);
                    releasedPcId = pc.Id;
                }
            }

            // Log Audit
            await _auditService.LogAsync(new AuditEntry
            {
                OperatorId = operatorId,
                UserRole = "Operator",
                UserName = "System",
                Action = AuditActions.PaymentProcess,
                BranchId = branchId,
                TargetType = "bill",
                TargetId = bill.Id,
                // Every leg, not just cash. This used to read
                // `new { PaymentType, Total, Cash }`, so a Split of Rs 5 cash + Rs 5 online
                // was recorded as "Split, Total 10, Cash 5" and the other Rs 5 simply was not
                // there. Anyone reading the audit trail to settle a dispute could see the
                // money was short and nothing saying where it went.
                Details = new
                {
                    PaymentType = dto.PaymentType.ToString(),
                    Total = totalPayment,
                    Cash = dto.CashAmount,
                    Online = dto.OnlineAmount,
                    Wallet = dto.WalletAmount,
                    Credit = dto.CreditAmount,
                }
            });

            await _auditService.LogAsync(new AuditEntry
            {
                OperatorId = operatorId,
                UserRole = "Operator",
                UserName = "System",
                Action = AuditActions.BillComplete,
                BranchId = branchId,
                TargetType = "bill",
                TargetId = bill.Id,
                Details = new { BillNumber = bill.BillNumber }
            });

            // Cash actually taken. This is the figure the owner reconciles the day against,
            // so it goes up with the split intact — gaming, food and discount separately,
            // plus how it was paid — rather than a single total Head Office cannot break down.
            await _outbox.RecordEventAsync(branchId, "Bill", bill.Id, "bill.paid", new
            {
                billId = bill.Id,
                billNumber = bill.BillNumber,
                sessionId = completedSessionId,
                operatorId,
                shiftId,
                paymentType = dto.PaymentType.ToString(),
                // Each leg travels as itself. Only cashAmount used to go up, and Head Office
                // reconstructed the rest as `totalPaid - cash`, guessing from the payment type
                // whether that remainder was online or wallet. For a Split it could only ever
                // guess one of them, so a Rs 5 + Rs 5 split arrived as a bill Head Office
                // recorded as Rs 10 cash - overstating the drawer and losing the online
                // settlement, on every split bill the company has ever taken.
                cashAmount = dto.CashAmount,
                onlineAmount = dto.OnlineAmount,
                walletAmount = dto.WalletAmount,
                creditAmount = dto.CreditAmount,
                actualCashCollected,
                totalPaid = totalPayment,
                gamingAmount = bill.GamingAmount,
                foodAmount = bill.FoodAmount,
                discountAmount = bill.DiscountAmount,
                billTotal = bill.TotalAmount,
                paidAt = DateTimeOffset.UtcNow,
            });

            await _unitOfWork.CommitTransactionAsync();
            await _hubNotification.BroadcastBillingUpdateAsync(branchId, bill.Id);
            
            if (completedSessionId.HasValue)
                await _hubNotification.BroadcastSessionUpdateAsync(branchId, completedSessionId.Value);
            
            if (releasedPcId.HasValue)
                await _hubNotification.BroadcastPcStatusChangeAsync(branchId, releasedPcId.Value);

            return MapToDto(bill);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<BillDto> RemoveBillItemAsync(Guid branchId, Guid operatorId, Guid billId, Guid billItemId)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var bill = await _unitOfWork.Repository<Bill>().Query()
                .Include(b => b.Items)
                .Include(b => b.Payments)
                .Include(b => b.Pc)
                .FirstOrDefaultAsync(b => b.Id == billId && b.BranchId == branchId)
                ?? throw new NotFoundException("Bill not found.");

            if (bill.Status == BillStatus.Completed)
                throw new AppException("Cannot modify a completed bill.");

            var itemToRemove = bill.Items.FirstOrDefault(i => i.Id == billItemId)
                ?? throw new NotFoundException("Bill item not found.");

            if (itemToRemove.ItemType.ToLower() == "gaming")
                throw new AppException("Cannot manually remove gaming items.");

            // Restore Inventory if applicable
            if (itemToRemove.InventoryId.HasValue)
            {
                var inventoryItem = await _unitOfWork.Repository<InventoryItem>().GetByIdAsync(itemToRemove.InventoryId.Value);
                if (inventoryItem != null)
                {
                    inventoryItem.CurrentStock += itemToRemove.Quantity;
                    inventoryItem.SoldQty -= itemToRemove.Quantity;
                    inventoryItem.UpdatedAt = DateTimeOffset.UtcNow;
                    _unitOfWork.Repository<InventoryItem>().Update(inventoryItem);

                    var log = new InventoryLog
                    {
                        InventoryId = inventoryItem.Id,
                        OperatorId = operatorId,
                        BranchId = branchId,
                        Action = "void_return",
                        Quantity = itemToRemove.Quantity,
                        Reason = "Item removed from bill",
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    await _unitOfWork.Repository<InventoryLog>().AddAsync(log);
                }
            }

            // Adjust Bill Totals
            bill.FoodAmount -= itemToRemove.TotalPrice;
            if (bill.FoodAmount < 0) bill.FoodAmount = 0;
            
            bill.Subtotal -= itemToRemove.TotalPrice;
            if (bill.Subtotal < 0) bill.Subtotal = 0;

            // Recalculate discount if percentage based
            if (bill.DiscountType == DiscountType.Percentage && bill.Subtotal > 0)
            {
                bill.DiscountAmount = bill.Subtotal * (bill.DiscountValue / 100);
            }
            else if (bill.DiscountType == DiscountType.Flat)
            {
                // Ensure flat discount doesn't exceed new subtotal
                if (bill.DiscountAmount > bill.Subtotal)
                    bill.DiscountAmount = bill.Subtotal;
            }

            var (displayGaming, displayFood, roundedTotal) = Application.Services.SessionPricingCalculator.ComputeRoundedBreakdown(
                bill.GamingAmount, bill.FoodAmount, bill.DiscountAmount);
            bill.GamingAmount = displayGaming;
            bill.FoodAmount = displayFood;
            bill.Subtotal = displayGaming + displayFood;
            bill.TotalAmount = roundedTotal;

            var removalGamingItem = bill.Items.FirstOrDefault(i => i.ItemType == "gaming");
            if (removalGamingItem != null)
            {
                removalGamingItem.TotalPrice = displayGaming;
                removalGamingItem.UnitPrice = displayGaming;
            }

            bill.UpdatedAt = DateTimeOffset.UtcNow;

            bill.Items.Remove(itemToRemove);
            _unitOfWork.Repository<BillItem>().Remove(itemToRemove);
            _unitOfWork.Repository<Bill>().Update(bill);

            await _auditService.LogAsync(new AuditEntry
            {
                OperatorId = operatorId,
                UserRole = "Operator",
                UserName = "System",
                Action = "bill_item_removed",
                BranchId = branchId,
                TargetType = "bill",
                TargetId = bill.Id,
                Details = new { ItemName = itemToRemove.ItemName, Quantity = itemToRemove.Quantity, Amount = itemToRemove.TotalPrice }
            });

            await _unitOfWork.CommitTransactionAsync();
            await _hubNotification.BroadcastBillingUpdateAsync(branchId, bill.Id);

            return MapToDto(bill);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    private static BillDto MapToDto(Bill b)
    {
        return new BillDto
        {
            Id = b.Id,
            BillNumber = b.BillNumber,
            SessionId = b.SessionId,
            PcId = b.PcId,
            PcNumber = b.Pc?.PcNumber,
            BranchId = b.BranchId,
            OperatorId = b.OperatorId,
            ShiftId = b.ShiftId,
            CustomerName = b.CustomerName,
            MemberId = b.MemberId,
            GamingAmount = b.GamingAmount,
            FoodAmount = b.FoodAmount,
            Subtotal = b.Subtotal,
            DiscountType = b.DiscountType,
            DiscountValue = b.DiscountValue,
            DiscountAmount = b.DiscountAmount,
            DiscountReason = b.DiscountReason,
            TotalAmount = b.TotalAmount,
            Status = b.Status,
            IsDeferred = b.IsDeferred,
            CreatedAt = b.CreatedAt,
            SessionEndTime = b.Session?.EndTime,
            Items = b.Items?.Select(i => new BillItemDto
            {
                Id = i.Id,
                ItemType = i.ItemType,
                ItemName = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice
            }).ToList() ?? new List<BillItemDto>(),
            Payments = b.Payments?.Select(p => new PaymentDto
            {
                Id = p.Id,
                PaymentType = p.PaymentType,
                TotalAmount = p.TotalAmount,
                CashAmount = p.CashAmount,
                OnlineAmount = p.OnlineAmount,
                WalletAmount = p.WalletAmount,
                CashReceived = p.CashReceived,
                ChangeReturned = p.ChangeReturned,
                ActualCashCollected = p.ActualCashCollected,
                CreatedAt = p.CreatedAt
            }).ToList() ?? new List<PaymentDto>()
        };
    }
}
