using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Reactor;
using System;

namespace LobbyUtils;

[BepInAutoPlugin]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
public partial class LobbyUtilsPlugin : BasePlugin
{
    public Harmony Harmony { get; } = new(Id);
    public static BepInEx.Logging.ManualLogSource Log { get; private set; } = null!;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("LobbyUtils is loaded!");

        // Parse CLI args
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--lobby-code" && i + 1 < args.Length)
            {
                LobbyManager.TargetLobbyCode = args[i + 1];
                Log.LogInfo($"Parsed lobby code from CLI: {LobbyManager.TargetLobbyCode}");
            }
            if (args[i] == "--server-ip" && i + 1 < args.Length)
            {
                LobbyManager.TargetServerIp = args[i + 1];
                Log.LogInfo($"Parsed server IP from CLI: {LobbyManager.TargetServerIp}");
            }
            if (args[i] == "--server-port" && i + 1 < args.Length)
            {
                if (ushort.TryParse(args[i + 1], out ushort port))
                {
                    LobbyManager.TargetServerPort = port;
                    Log.LogInfo($"Parsed server port from CLI: {LobbyManager.TargetServerPort}");
                }
            }
        }

        // Start IPC Server
        IPCServer.Start(Log);

        // Apply Harmony patches
        Harmony.PatchAll();
    }
}
