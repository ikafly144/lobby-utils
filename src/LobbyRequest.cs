namespace LobbyUtils;

public readonly record struct LobbyRequest(
    string LobbyCode,
    string? ServerIp,
    ushort? ServerPort
);
