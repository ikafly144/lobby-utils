using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Runtime.Versioning;

namespace LobbyUtils;

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInProcess("Among Us.exe")]
[SupportedOSPlatform("windows")]
public class LobbyUtilsPlugin : BasePlugin
{
    public const string PluginId = "com.ikafly.lobbyutils";
    public const string PluginName = "LobbyUtils";
    public const string PluginVersion = "1.0.0";

    public Harmony Harmony { get; } = new(PluginId);

    public static BepInEx.Logging.ManualLogSource PluginLog { get; private set; } = null!;

    public override void Load()
    {
        PluginLog = base.Log;
        PluginLog.LogInfo("LobbyUtils loaded.");

        var initialRequest = CommandLineParser.Parse(Environment.GetCommandLineArgs(), PluginLog);
        if (initialRequest is not null)
        {
            LobbyManager.Enqueue(initialRequest.Value, "CLI");
        }

        IPCServer.Start(PluginLog);
        Harmony.PatchAll();
    }
}

