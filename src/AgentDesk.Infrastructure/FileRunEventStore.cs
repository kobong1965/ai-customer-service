using System.Text.Json;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileRunEventStore
{
    private const int MaximumEvents = 5000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly string _filePath;

    public FileRunEventStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "logs",
            "run-events.jsonl");
    }

    public void Append(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("运行日志路径无效。");
            Directory.CreateDirectory(directory);
            var sanitized = runEvent with
            {
                AccountId = Sanitize(runEvent.AccountId, 80),
                Summary = Sanitize(runEvent.Summary, 500)
            };
            File.AppendAllText(
                _filePath,
                JsonSerializer.Serialize(sanitized, JsonOptions) + Environment.NewLine);
            TrimIfNeeded();
        }
    }

    public string ExportJsonLines()
    {
        lock (_sync)
        {
            try
            {
                return File.Exists(_filePath) ? File.ReadAllText(_filePath) : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    public IReadOnlyList<RunEvent> ReadRecent(int count = 50)
    {
        if (count <= 0 || !File.Exists(_filePath))
        {
            return [];
        }

        lock (_sync)
        {
            try
            {
                return File.ReadLines(_filePath)
                    .TakeLast(count)
                    .Select(TryDeserialize)
                    .Where(item => item is not null)
                    .Cast<RunEvent>()
                    .Reverse()
                    .ToArray();
            }
            catch (IOException)
            {
                return [];
            }
        }
    }

    private static RunEvent? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<RunEvent>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void TrimIfNeeded()
    {
        var lines = File.ReadLines(_filePath).Take(MaximumEvents + 1).Count();
        if (lines <= MaximumEvents)
        {
            return;
        }

        var kept = File.ReadLines(_filePath).TakeLast(MaximumEvents).ToArray();
        File.WriteAllLines(_filePath, kept);
    }

    private static string Sanitize(string value, int maximumLength)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maximumLength ? singleLine : singleLine[..maximumLength];
    }
}
