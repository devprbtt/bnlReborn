namespace BNLReloadedServer.ServerTypes;

public record QueueSnapshot(
    string ModeId,
    string? ModeName,
    int PlayerCount,
    string State,
    ulong? ConfirmDeadline,
    List<QueuedPlayerSnapshot> Players);

public record QueuedPlayerSnapshot(
    uint PlayerId,
    string? Nickname,
    long JoinTime,
    bool Confirming,
    string Mode);
