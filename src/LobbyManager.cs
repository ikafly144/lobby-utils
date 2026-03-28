using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using InnerNet;
using System;

namespace LobbyUtils;

public static class LobbyManager
{
    public static string? TargetLobbyCode { get; set; }
    public static string? TargetServerIp { get; set; }
    public static ushort? TargetServerPort { get; set; }

    public static bool ShouldAttemptConnection { get; set; } = false;

    // Call this from a safe place (e.g. main menu patch) to execute the join
    public static void AttemptConnection()
    {
        if (AmongUsClient.Instance == null || AmongUsClient.Instance.GameState != InnerNetClient.GameStates.NotJoined)
        {
            return; // Not in a state to join
        }

        if (!string.IsNullOrEmpty(TargetServerIp) && TargetServerPort.HasValue)
        {
            // Modern Among Us uses ServerManager, but direct IP connection can be set via EndPoint
            AmongUsClient.Instance.SetEndpoint(TargetServerIp, TargetServerPort.Value);
            TargetServerIp = null;
            TargetServerPort = null;
        }

        if (!string.IsNullOrEmpty(TargetLobbyCode))
        {
            if (TryConvertCode(TargetLobbyCode, out int gameId))
            {
                AmongUsClient.Instance.GameMode = GameModes.OnlineGame;
                AmongUsClient.Instance.JoinGame(gameId);
                LobbyUtilsPlugin.Log.LogInfo($"Attempting to join game: {TargetLobbyCode} ({gameId})");
            }
            else
            {
                LobbyUtilsPlugin.Log.LogError($"Invalid lobby code format: {TargetLobbyCode}");
            }
            TargetLobbyCode = null;
        }

        ShouldAttemptConnection = false;
    }

    private static bool TryConvertCode(string code, out int result)
    {
        try 
        {
            // Among Us InnerNet.GameCode.GameNameToInt
            result = InnerNet.GameCode.GameNameToInt(code);
            return true;
        }
        catch 
        {
            result = 0;
            return false;
        }
    }
}

// Patch MainMenuManager or a similar persistent updater to poll the state
[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Update))]
public static class MainMenuManagerPatch
{
    public static void Postfix()
    {
        // Poll for IPC changes or initial CLI args
        if (LobbyManager.TargetLobbyCode != null || LobbyManager.TargetServerIp != null)
        {
            LobbyManager.ShouldAttemptConnection = true;
        }

        if (LobbyManager.ShouldAttemptConnection)
        {
            LobbyManager.AttemptConnection();
        }
    }
}
