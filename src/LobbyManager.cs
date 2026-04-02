using HarmonyLib;
using Il2CppInterop.Runtime;
using InnerNet;
using System.Collections.Concurrent;
using System.Linq.Expressions;
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
    bool? IsInGame,
    string? MatchMakerIp,
    int? MatchMakerPort
);

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class LobbyManager
{
    private static readonly ConcurrentQueue<LobbyRequest> PendingRequests = new();
    private static readonly object ConnectionInfoLock = new();
    private static readonly object EndpointLock = new();
    private static string? _lastRequestedServerIp;
    private static int? _lastRequestedServerPort;
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
        IsInGame: null,
        MatchMakerIp: null,
        MatchMakerPort: null);
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
            ProcessRequest(request);
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
                    IsInGame: null,
                    MatchMakerIp: null,
                    MatchMakerPort: null);
            }
            return;
        }

        var client = AmongUsClient.Instance;
        string? lobbyCode = TryFormatLobbyCode(client.GameId);
        string? serverIp = GetServerIp();
        int? serverPort = GetServerPort();
        int? gameId = client.GameId != 0 ? client.GameId : null;
        bool isConnected = client.GameState != InnerNetClient.GameStates.NotJoined;
        var server = FastDestroyableSingleton<ServerManager>.Instance.CurrentRegion.Servers.FirstOrDefault();

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
                IsInGame: client.IsInGame,
                MatchMakerIp: server?.Ip,
                MatchMakerPort: server?.Port);
        }
    }

    private static string? GetServerIp()
    {
        try
        {
            if (AmongUsClient.Instance.connection == null)
            {
                lock (EndpointLock)
                {
                    return _lastRequestedServerIp;
                }
            }

            var serverIp = AmongUsClient.Instance.networkAddress;
            if (serverIp != null)
            {
                string? address = serverIp.Contains(':') ? $"[{serverIp}]" : serverIp; // Format IPv6 addresses with brackets for consistency
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }

            lock (EndpointLock)
            {
                return _lastRequestedServerIp;
            }
        }
        catch (System.Exception ex)
        {
            LobbyUtilsPlugin.PluginLog.LogWarning($"Failed to get server IP: {ex.Message}");
            lock (EndpointLock)
            {
                return _lastRequestedServerIp;
            }
        }
    }

    private static int? GetServerPort()
    {
        try
        {
            if (AmongUsClient.Instance.connection == null)
            {
                lock (EndpointLock)
                {
                    return _lastRequestedServerPort;
                }
            }

            var serverPort = AmongUsClient.Instance.networkPort;
            if (serverPort > 0)
            {
                return serverPort;
            }

            lock (EndpointLock)
            {
                return _lastRequestedServerPort;
            }
        }
        catch (System.Exception ex)
        {
            LobbyUtilsPlugin.PluginLog.LogWarning($"Failed to get server port: {ex.Message}");
            lock (EndpointLock)
            {
                return _lastRequestedServerPort;
            }
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

    private static void ProcessRequest(LobbyRequest request)
    {
        bool hasCode = !string.IsNullOrWhiteSpace(request.LobbyCode);
        bool hasMatchMaker = !string.IsNullOrWhiteSpace(request.MatchMakerIp) && request.MatchMakerPort.HasValue;

        if (hasMatchMaker)
        {
            lock (EndpointLock)
            {
                _lastRequestedServerIp = request.ServerIp;
                _lastRequestedServerPort = request.ServerPort;
            }
            FastDestroyableSingleton<ServerManager>.Instance.SetRegion(GetRegion(request.MatchMakerIp!, request.MatchMakerPort!.Value));
            LobbyUtilsPlugin.PluginLog.LogInfo($"Set custom matchmaker region to {request.MatchMakerIp}:{request.MatchMakerPort} for lobby code {request.LobbyCode}");
        }

        if (!hasCode)
        {
            if (hasMatchMaker)
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

        if (hasMatchMaker)
        {
            LobbyUtilsPlugin.PluginLog.LogInfo($"Attempting lobby join with custom endpoint: {request.LobbyCode} via {request.ServerIp}:{request.ServerPort}");
            return;
        }

        AmongUsClient.Instance.StartCoroutine(AmongUsClient.Instance.CoJoinOnlinePublicGame(gameId, request.ServerIp, request.ServerPort, AmongUsClient.MainMenuTarget.OnlineMenu));
        LobbyUtilsPlugin.PluginLog.LogInfo($"Attempting lobby join: {request.LobbyCode} ({gameId})");
    }

    private static IRegionInfo? GetRegion(string matchmakerIP, ushort matchmakerPort)
    {
        string prefix = "";
        if (!matchmakerIP.StartsWith("http"))
            prefix = $"http{(matchmakerPort == 443 ? "s" : "")}://";
        return new StaticHttpRegionInfo($"{matchmakerIP}:{matchmakerPort}", StringNames.NoTranslation,
                matchmakerIP, new(
                    [
                        new("http-1", $"{prefix}{matchmakerIP}",
                        matchmakerPort, false)
                    ]
                )
            ).TryCast<IRegionInfo>();
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

public static unsafe class FastDestroyableSingleton<T> where T : MonoBehaviour
{
    private static readonly IntPtr _fieldPtr;
    private static readonly Func<IntPtr, T> _createObject;
    static FastDestroyableSingleton()
    {
        _fieldPtr = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<DestroyableSingleton<T>>.NativeClassPtr, nameof(DestroyableSingleton<T>._instance));
        var constructor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
        var ptr = Expression.Parameter(typeof(IntPtr));
        var create = Expression.New(constructor!, ptr);
        var lambda = Expression.Lambda<Func<IntPtr, T>>(create, ptr);
        _createObject = lambda.Compile();
    }

    public static T Instance
    {
        get
        {
            IntPtr objectPointer;
            IL2CPP.il2cpp_field_static_get_value(_fieldPtr, &objectPointer);
            return objectPointer == IntPtr.Zero ? DestroyableSingleton<T>.Instance : _createObject(objectPointer);
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
