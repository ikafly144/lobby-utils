using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;

namespace LobbyUtils;

public class IPCMessage
{
    public string Action { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Ip { get; set; }
    public ushort? Port { get; set; }
}

public static class IPCServer
{
    private static ManualLogSource? Logger;

    public static void Start(ManualLogSource logger)
    {
        Logger = logger;
        Task.Run(RunServerLoop);
    }

    private static async Task RunServerLoop()
    {
        while (true)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    "LobbyUtilsPipe", 
                    PipeDirection.In, 
                    1, 
                    PipeTransmissionMode.Message, 
                    PipeOptions.Asynchronous);

                Logger?.LogInfo("IPC Server waiting for connection...");
                await pipeServer.WaitForConnectionAsync();

                using var reader = new StreamReader(pipeServer);
                string? message = await reader.ReadLineAsync();

                if (!string.IsNullOrEmpty(message))
                {
                    Logger?.LogInfo($"Received IPC message: {message}");
                    ProcessMessage(message);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"IPC Server Error: {ex}");
                await Task.Delay(1000); // Prevent tight loop on error
            }
        }
    }

    private static void ProcessMessage(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<IPCMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (msg == null) return;

            if (msg.Action.Equals("join", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(msg.Code))
                {
                    LobbyManager.TargetLobbyCode = msg.Code;
                    LobbyManager.ShouldAttemptConnection = true;
                    Logger?.LogInfo($"Queued lobby join from IPC: {msg.Code}");
                }
                else if (!string.IsNullOrEmpty(msg.Ip) && msg.Port.HasValue)
                {
                    LobbyManager.TargetServerIp = msg.Ip;
                    LobbyManager.TargetServerPort = msg.Port.Value;
                    LobbyManager.ShouldAttemptConnection = true;
                    Logger?.LogInfo($"Queued custom server join from IPC: {msg.Ip}:{msg.Port}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"Error parsing IPC message: {ex}");
        }
    }
}
