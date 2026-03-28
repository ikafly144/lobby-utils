using System.Runtime.Versioning;
using BepInEx.Logging;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

namespace LobbyUtils;

public sealed class IPCMessage
{
    public string Action { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Ip { get; set; }
    public ushort? Port { get; set; }
}

[SupportedOSPlatform("windows")]
public sealed class IPCResponse
{
    public bool Success { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Message { get; set; }
    public object? Data { get; set; }
}

[SupportedOSPlatform("windows")]
public static class IPCServer
{
    private const string PipeNamePrefix = "LobbyUtilsPipe";
    public static string PipeName { get; } = $"{PipeNamePrefix}-{Environment.ProcessId}";
    private static ManualLogSource? _logger;

    public static void Start(ManualLogSource logger)
    {
        _logger = logger;
        _logger.LogInfo($"IPC server started on pipe '{PipeName}' (pid={Environment.ProcessId}).");
        Task.Run(RunServerLoop);
    }

    private static async Task RunServerLoop()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync();

                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                var payload = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    ProcessMessage(payload, writer);
                }
            }
            catch (IOException ioEx)
            {
                _logger?.LogWarning($"IPC I/O error: {ioEx.Message}");
                await Task.Delay(500);
            }
            catch (UnauthorizedAccessException authEx)
            {
                _logger?.LogError($"IPC permission error: {authEx.Message}");
                await Task.Delay(1000);
            }
            catch (InvalidOperationException invalidEx)
            {
                _logger?.LogError($"IPC invalid operation: {invalidEx.Message}");
                await Task.Delay(500);
            }
        }
    }

    private static void ProcessMessage(string json, StreamWriter writer)
    {
        IPCMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<IPCMessage>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException jsonEx)
        {
            _logger?.LogWarning($"Ignored invalid IPC JSON: {jsonEx.Message}");
            WriteResponse(
                writer,
                new IPCResponse
                {
                    Success = false,
                    Action = "unknown",
                    Message = $"Invalid JSON: {jsonEx.Message}"
                });
            return;
        }

        if (message is null)
        {
            _logger?.LogWarning("Ignored empty IPC payload.");
            WriteResponse(
                writer,
                new IPCResponse
                {
                    Success = false,
                    Action = "unknown",
                    Message = "Empty payload."
                });
            return;
        }

        if (string.Equals(message.Action, "join", StringComparison.OrdinalIgnoreCase))
        {
            ProcessJoin(message, writer);
            return;
        }

        if (string.Equals(message.Action, "getLobbyInfo", StringComparison.OrdinalIgnoreCase))
        {
            var info = LobbyManager.GetConnectionInfo();
            WriteResponse(
                writer,
                new IPCResponse
                {
                    Success = true,
                    Action = "getLobbyInfo",
                    Data = info
                });
            return;
        }

        _logger?.LogWarning($"Ignored unknown IPC action: {message.Action}");
        WriteResponse(
            writer,
            new IPCResponse
            {
                Success = false,
                Action = message.Action,
                Message = $"Unknown action: {message.Action}"
            });
    }

    private static void ProcessJoin(IPCMessage message, StreamWriter writer)
    {
        if (!string.IsNullOrWhiteSpace(message.Code))
        {
            LobbyManager.Enqueue(new LobbyRequest(message.Code, null, null), "IPC");
            WriteResponse(
                writer,
                new IPCResponse
                {
                    Success = true,
                    Action = "join",
                    Message = "Queued lobby join by code."
                });
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.Ip) && message.Port.HasValue)
        {
            LobbyManager.Enqueue(new LobbyRequest(null, message.Ip, message.Port.Value), "IPC");
            WriteResponse(
                writer,
                new IPCResponse
                {
                    Success = true,
                    Action = "join",
                    Message = "Queued server endpoint join."
                });
            return;
        }

        _logger?.LogWarning("Ignored IPC join message without code or endpoint.");
        WriteResponse(
            writer,
            new IPCResponse
            {
                Success = false,
                Action = "join",
                Message = "Join requires either code or (ip + port)."
            });
    }

    private static void WriteResponse(StreamWriter writer, IPCResponse response)
    {
        try
        {
            var json = JsonSerializer.Serialize(response);
            writer.WriteLine(json);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to write IPC response: {ex.Message}");
        }
    }
}
