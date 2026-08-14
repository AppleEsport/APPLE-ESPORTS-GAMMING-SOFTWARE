namespace AppleEsportsErp.Application.DTOs.Sync;

/// <summary>
/// Something Head Office is asking this branch to do, riding down in the heartbeat reply
/// exactly like settings do - the same three-second round trip, no second connection.
/// </summary>
public class BranchCommandDto
{
    public Guid Id { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
}

/// <summary>
/// What the branch reports back after carrying a command out. This is the only thing that is
/// ever allowed to close a command - Head Office asked, it does not get to decide the answer.
/// </summary>
public class BranchCommandResultDto
{
    public Guid CommandId { get; set; }
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
}
