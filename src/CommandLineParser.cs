using BepInEx.Logging;
using System;

namespace LobbyUtils;

public static class CommandLineParser
{
    public static LobbyRequest? Parse(string[] args, ManualLogSource logger)
    {
        string? lobbyCode = null;
        string? serverIp = null;
        ushort? serverPort = null;
        string? matchMakerIp = null;
        ushort? matchMakerPort = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--lobby-code", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                lobbyCode = args[i + 1];
                i++;
                continue;
            }

            if (string.Equals(args[i], "--server-ip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverIp = args[i + 1];
                i++;
                continue;
            }

            if (string.Equals(args[i], "--server-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (ushort.TryParse(args[i + 1], out var parsed))
                {
                    serverPort = parsed;
                }
                else
                {
                    logger.LogWarning($"Ignored invalid --server-port value: {args[i + 1]}");
                }
                i++;
            }

            if (string.Equals(args[i], "--matchmaker-ip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                matchMakerIp = args[i + 1];
                i++;
                continue;
            }

            if (string.Equals(args[i], "--matchmaker-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (ushort.TryParse(args[i + 1], out var parsed))
                {
                    matchMakerPort = parsed;
                }
                else
                {
                    logger.LogWarning($"Ignored invalid --matchmaker-port value: {args[i + 1]}");
                }
                i++;
            }
        }

        if (!string.IsNullOrWhiteSpace(lobbyCode) && !string.IsNullOrWhiteSpace(serverIp) && serverPort.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(matchMakerIp) && matchMakerPort.HasValue)
            {
                logger.LogInfo($"Parsed CLI lobby code, server endpoint, and matchmaker endpoint: {lobbyCode} via {serverIp}:{serverPort.Value} with matchmaker {matchMakerIp}:{matchMakerPort.Value}");
                return new LobbyRequest(lobbyCode, serverIp, serverPort.Value, matchMakerIp, matchMakerPort.Value);
            }
            if (!string.IsNullOrWhiteSpace(matchMakerIp) || matchMakerPort.HasValue)
            {
                logger.LogWarning("Both --matchmaker-ip and --matchmaker-port are required to use custom matchmaker join.");
            }
            logger.LogInfo($"Parsed CLI lobby code and server endpoint: {lobbyCode} via {serverIp}:{serverPort.Value}");
            return new LobbyRequest(lobbyCode, serverIp, serverPort.Value, null, null);
        }

        return null;
    }
}
