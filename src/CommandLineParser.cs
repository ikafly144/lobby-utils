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
        }

        if (!string.IsNullOrWhiteSpace(lobbyCode))
        {
            if (!string.IsNullOrWhiteSpace(serverIp) && serverPort.HasValue)
            {
                logger.LogInfo($"Parsed CLI server endpoint: {serverIp}:{serverPort.Value}");
                return new LobbyRequest(lobbyCode, serverIp, serverPort.Value);
            }
            if (!string.IsNullOrWhiteSpace(serverIp) || serverPort.HasValue)
            {
                logger.LogWarning("Both --server-ip and --server-port are required to use custom server join.");
            }
            logger.LogInfo($"Parsed CLI lobby code: {lobbyCode}");
            return new LobbyRequest(lobbyCode, null, null);
        }

        return null;
    }
}
