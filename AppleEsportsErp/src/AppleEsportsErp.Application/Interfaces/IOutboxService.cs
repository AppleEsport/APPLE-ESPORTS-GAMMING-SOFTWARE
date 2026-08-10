namespace AppleEsportsErp.Application.Interfaces;

public interface IOutboxService
{
    Task RecordEventAsync(
        Guid branchId,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        object eventData);
}
