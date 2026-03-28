using HarmonyLib;
using InnerNet;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;

namespace LobbyUtils;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public readonly record struct LobbyConnectionInfo(
    bool HasClient,
    bool IsConnected,
    string GameState,
    int? GameId,
    string? LobbyCode,
    string? ServerIp,
    int? ServerPort,
    int? ClientId,
    int? HostId,
    bool? IsHost,
    bool? IsInGame
);

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class LobbyManager
{
    private static readonly ConcurrentQueue<LobbyRequest> PendingRequests = new();
    private static readonly PropertyInfo? NetworkAddressProperty = AccessTools.Property(typeof(AmongUsClient), "networkAddress");
    private static readonly FieldInfo? NetworkAddressField = AccessTools.Field(typeof(AmongUsClient), "networkAddress");
    private static readonly PropertyInfo? NetworkPortProperty = AccessTools.Property(typeof(AmongUsClient), "networkPort");
    private static readonly FieldInfo? NetworkPortField = AccessTools.Field(typeof(AmongUsClient), "networkPort");
    private static readonly object ConnectionInfoLock = new();
    private static LobbyConnectionInfo _connectionInfo = new(
        HasClient: false,
        IsConnected: false,
        GameState: InnerNetClient.GameStates.NotJoined.ToString(),
        GameId: null,
        LobbyCode: null,
        ServerIp: null,
        ServerPort: null,
        ClientId: null,
        HostId: null,
        IsHost: null,
        IsInGame: null);
    private static int? _lastGameIdConversionError;

    public static LobbyConnectionInfo GetConnectionInfo()
    {
        lock (ConnectionInfoLock)
        {
            return _connectionInfo;
        }
    }

    public static void RefreshConnectionInfo()
    {
        UpdateConnectionInfo();
    }

    public static void Enqueue(LobbyRequest request, string source)
    {
        PendingRequests.Enqueue(request);
        LobbyUtilsPlugin.PluginLog.LogInfo($"Queued join request from {source}.");
    }

    public static void DrainAndAttemptConnection()
    {
        RefreshConnectionInfo();

        if (AmongUsClient.Instance == null)
        {
            return;
        }

        if (AmongUsClient.Instance.GameState != InnerNetClient.GameStates.NotJoined)
        {
            return;
        }

        while (PendingRequests.TryDequeue(out var request))
        {
            ProcessRequest(AmongUsClient.Instance, request);
        }
    }

    private static void UpdateConnectionInfo()
    {
        if (AmongUsClient.Instance == null)
        {
            lock (ConnectionInfoLock)
            {
                _connectionInfo = new LobbyConnectionInfo(
                    HasClient: false,
                    IsConnected: false,
                    GameState: InnerNetClient.GameStates.NotJoined.ToString(),
                    GameId: null,
                    LobbyCode: null,
                    ServerIp: null,
                    ServerPort: null,
                    ClientId: null,
                    HostId: null,
                    IsHost: null,
                    IsInGame: null);
            }
            return;
        }

        var client = AmongUsClient.Instance;
        string? lobbyCode = TryFormatLobbyCode(client.GameId);
        string? serverIp = TryGetServerAddress(client);
        int? serverPort = TryGetServerPort(client);
        int? gameId = client.GameId != 0 ? client.GameId : null;
        bool isConnected = client.GameState != InnerNetClient.GameStates.NotJoined;

        lock (ConnectionInfoLock)
        {
            _connectionInfo = new LobbyConnectionInfo(
                HasClient: true,
                IsConnected: isConnected,
                GameState: client.GameState.ToString(),
                GameId: gameId,
                LobbyCode: lobbyCode,
                ServerIp: serverIp,
                ServerPort: serverPort,
                ClientId: client.ClientId,
                HostId: client.HostId,
                IsHost: client.AmHost,
                IsInGame: client.IsInGame);
        }
    }

    private static string? TryFormatLobbyCode(int gameId)
    {
        if (gameId == 0)
        {
            return null;
        }

        try
        {
            _lastGameIdConversionError = null;
            return GameCode.IntToGameName(gameId);
        }
        catch (System.Exception ex)
        {
            if (_lastGameIdConversionError != gameId)
            {
                _lastGameIdConversionError = gameId;
                LobbyUtilsPlugin.PluginLog.LogWarning($"Lobby code conversion failed for gameId={gameId}: {ex.Message}");
            }

            return null;
        }
    }

    private static string? TryGetServerAddress(AmongUsClient client)
    {
        var raw = NetworkAddressProperty?.GetValue(client) ?? NetworkAddressField?.GetValue(client);
        if (raw is not string address || string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return address;
    }

    private static int? TryGetServerPort(AmongUsClient client)
    {
        var raw = NetworkPortProperty?.GetValue(client) ?? NetworkPortField?.GetValue(client);
        if (raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw.ToString(), out int port) || port <= 0)
        {
            return null;
        }

        return port;
    }

    private static void ProcessRequest(AmongUsClient client, LobbyRequest request)
    {
        bool hasCode = !string.IsNullOrWhiteSpace(request.LobbyCode);
        bool hasEndpoint = !string.IsNullOrWhiteSpace(request.ServerIp) && request.ServerPort.HasValue;

        if (hasEndpoint)
        {
            client.SetEndpoint(request.ServerIp!, request.ServerPort!.Value, dtls: false);
            LobbyUtilsPlugin.PluginLog.LogInfo($"Applied server endpoint: {request.ServerIp}:{request.ServerPort.Value}");
        }

        if (!hasCode)
        {
            if (hasEndpoint)
            {
                LobbyUtilsPlugin.PluginLog.LogWarning("Ignored endpoint-only request. Lobby code is required for joining.");
            }
            return;
        }

        if (!TryConvertCode(request.LobbyCode!, out int gameId))
        {
            LobbyUtilsPlugin.PluginLog.LogError($"Invalid lobby code: {request.LobbyCode}");
            return;
        }

        if (hasEndpoint)
        {
            ushort port = request.ServerPort ?? 0;
            client.StartCoroutine(client.CoJoinOnlineGameFromCode(gameId, fromEnterCode: true));
            LobbyUtilsPlugin.PluginLog.LogInfo($"Attempting lobby join with custom endpoint: {request.LobbyCode} via {request.ServerIp}:{port}");
            return;
        }

        client.StartCoroutine(client.CoFindGameInfoFromCodeAndJoin(gameId));
        LobbyUtilsPlugin.PluginLog.LogInfo($"Attempting lobby join: {request.LobbyCode} ({gameId})");
    }

    private static bool TryConvertCode(string code, out int result)
    {
        try
        {
            result = GameCode.GameNameToInt(code);
            return result != 0;
        }
        catch (System.Exception ex)
        {
            LobbyUtilsPlugin.PluginLog.LogError($"Lobby code conversion failed: {ex.Message}");
            result = 0;
            return false;
        }
    }
}

[HarmonyPatch(typeof(MainMenuManager), "LateUpdate")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class MainMenuManagerPatch
{
    public static void Postfix()
    {
        LobbyManager.DrainAndAttemptConnection();
    }
}

[HarmonyPatch(typeof(AmongUsClient), "Update")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AmongUsClientUpdatePatch
{
    public static void Postfix()
    {
        LobbyManager.RefreshConnectionInfo();
    }
}
