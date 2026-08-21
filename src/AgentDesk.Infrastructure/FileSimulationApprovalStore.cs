using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileSimulationApprovalStore : ISimulationApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public FileSimulationApprovalStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "simulation-approval.json");
    }

    public async Task<SimulationApproval?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<SimulationApproval>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        SimulationApproval approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("批准记录路径无效。");

        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                approval,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}

public static class RuntimeConfiguration
{
    private const string SafetyConfiguration =
        "agentdesk-v1|simulation-suite-v1|risk-rules-v2|auto-send-low-risk-only|qwen-vision-contract-v2";

    public static string CurrentFingerprint { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(SafetyConfiguration)));
}
