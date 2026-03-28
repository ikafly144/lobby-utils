using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Runtime.Versioning;

namespace LobbyUtils;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Among Us.exe")]
[SupportedOSPlatform("windows")]
public class LobbyUtilsPlugin : BasePlugin
{
    public Harmony Harmony { get; } = new(MyPluginInfo.PLUGIN_GUID);

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

