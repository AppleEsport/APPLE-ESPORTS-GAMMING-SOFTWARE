namespace AppleEsportsErp.Application.Interfaces;

public interface IEmailQueueService
{
    Task<bool> IsHeadOfficeAsync();
    Task QueueEmailForBranchAsync(Guid branchId, string to, string subject, string body);
}
