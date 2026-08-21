using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed record ModelProviderSettings(
    string Endpoint,
    string Model,
    int TimeoutSeconds,
    bool UseRuleFallbackInSimulation)
{
    public static ModelProviderSettings Default { get; } = new(
        "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
        "qwen3.7-plus",
        60,
        true);
}

public sealed record PlatformCalibrationSettings(
    string WindowTitleContains,
    double InputX,
    double InputY,
    double SendX,
    double SendY,
    int PollIntervalMilliseconds,
    double MinimumObserverConfidence,
    int CapturedWidth = 0,
    int CapturedHeight = 0)
{
    public static PlatformCalibrationSettings Default { get; } = new(
        string.Empty,
        0.44,
        0.965,
        0.594,
        0.965,
        1500,
        0.85,
        0,
        0);

    public bool IsValid => !string.IsNullOrWhiteSpace(WindowTitleContains)
        && IsUnit(InputX) && IsUnit(InputY) && IsUnit(SendX) && IsUnit(SendY)
        && PollIntervalMilliseconds is >= 500 and <= 30000
        && MinimumObserverConfidence is >= 0.75 and <= 1;

    private static bool IsUnit(double value) => value is >= 0 and <= 1;

    public bool IsWindowSizeStable(int width, int height, double tolerance = 0.12)
    {
        if (CapturedWidth <= 0 || CapturedHeight <= 0)
        {
            return true;
        }

        var widthDrift = Math.Abs(width - CapturedWidth) / (double)CapturedWidth;
        var heightDrift = Math.Abs(height - CapturedHeight) / (double)CapturedHeight;
        return widthDrift <= tolerance && heightDrift <= tolerance;
    }
}

public sealed record RuntimeSafetySettings(
    AgentExecutionMode ExecutionMode,
    int DailySendLimit,
    int PerMinuteSendLimit)
{
    public static RuntimeSafetySettings Default { get; } = new(
        AgentExecutionMode.Shadow,
        100,
        6);

    public bool IsValid => new RuntimeSafetyLimits(DailySendLimit, PerMinuteSendLimit).IsValid;
}

public sealed record MemoryLearningSettings(
    bool AutoCaptureEnabled,
    int CandidateLimit)
{
    public static MemoryLearningSettings Default { get; } = new(true, 500);

    public bool IsValid => CandidateLimit is >= 50 and <= 5000;
}

public sealed record AppSettings(
    ModelProviderSettings Model,
    PlatformCalibrationSettings Platform,
    DateTimeOffset? ModelVerifiedAt,
    string? ModelVerifiedFingerprint,
    RuntimeSafetySettings? Safety = null,
    MemoryLearningSettings? Memory = null)
{
    public static AppSettings Default { get; } = new(
        ModelProviderSettings.Default,
        PlatformCalibrationSettings.Default,
        null,
        null,
        RuntimeSafetySettings.Default,
        MemoryLearningSettings.Default);
}

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    string? Read(string key);

    void Write(string key, string value);

    void Delete(string key);
}

public sealed class FileAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public FileAppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return AppSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            return settings is null
                ? AppSettings.Default
                : settings with
                {
                    Model = settings.Model ?? ModelProviderSettings.Default,
                    Platform = settings.Platform ?? PlatformCalibrationSettings.Default,
                    Safety = settings.Safety ?? RuntimeSafetySettings.Default,
                    Memory = settings.Memory ?? MemoryLearningSettings.Default
                };
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
        catch (IOException)
        {
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("设置文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }
}

public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private readonly string _prefix;

    public WindowsCredentialSecretStore(string prefix = "AgentDesk") => _prefix = prefix;

    public string? Read(string key)
    {
        if (!CredRead(Target(key), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == 1168 ? null : throw new InvalidOperationException($"读取 Windows 凭据失败：{error}");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Write(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = Encoding.Unicode.GetBytes(value);
        var blobPointer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blobPointer, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(key),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"保存 Windows 凭据失败：{Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blobPointer);
        }
    }

    public void Delete(string key)
    {
        if (!CredDelete(Target(key), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168)
            {
                throw new InvalidOperationException($"删除 Windows 凭据失败：{error}");
            }
        }
    }

    private string Target(string key) => $"{_prefix}:{key}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint credential);
}
