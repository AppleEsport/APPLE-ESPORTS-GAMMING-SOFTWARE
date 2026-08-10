using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AppleEsportsErp.Infrastructure.Services;

public class EmailQueueService : IEmailQueueService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailQueueService> _logger;
    private readonly bool _isHeadOffice;

    public EmailQueueService(IUnitOfWork unitOfWork, IConfiguration configuration, ILogger<EmailQueueService> logger)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
        _isHeadOffice = bool.TryParse(configuration["Deployment:IsHeadOffice"], out var isHO) && isHO;
    }

    public async Task<bool> IsHeadOfficeAsync()
    {
        return _isHeadOffice;
    }

    public async Task QueueEmailForBranchAsync(Guid branchId, string to, string subject, string body)
    {
        try
        {
            // Queue email as outbox event so Head Office can send it later
            var entry = new SyncOutboxEntry
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                AggregateType = "Email",
                AggregateId = Guid.NewGuid(),
                EventType = "email.send_requested",
                EventData = JsonSerializer.Serialize(new
                {
                    to,
                    subject,
                    body,
                    requestedAt = DateTime.UtcNow
                }),
                CreatedAt = DateTime.UtcNow,
                SyncedAt = null,
                AttemptCount = 0
            };

            await _unitOfWork.Repository<SyncOutboxEntry>().AddAsync(entry);
            await _unitOfWork.CommitTransactionAsync();

            _logger.LogInformation("Queued email for branch {BranchId} to {To} with subject {Subject}",
                branchId, to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing email for branch {BranchId}", branchId);
            throw;
        }
    }
}
