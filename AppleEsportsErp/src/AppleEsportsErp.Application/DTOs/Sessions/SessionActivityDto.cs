namespace AppleEsportsErp.Application.DTOs.Sessions;

public class SessionActivityDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Action { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal? Amount { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
