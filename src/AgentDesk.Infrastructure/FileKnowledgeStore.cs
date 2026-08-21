using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileKnowledgeStore : IKnowledgeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _filePath;

    public FileKnowledgeStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "knowledge.json");
    }

    public IReadOnlyList<KnowledgeItem> LoadAll()
    {
        lock (_sync)
        {
            return LoadUnsafe();
        }
    }

    public KnowledgeItem AddReviewed(string title, string content, string accountScope)
    {
        ValidateFields(title, content, accountScope);
        var item = new KnowledgeItem(
            $"kb:local:{Guid.NewGuid():N}",
            title.Trim(),
            content.Trim(),
            string.IsNullOrWhiteSpace(accountScope) ? "全部账号" : accountScope.Trim(),
            true,
            true,
            DateTimeOffset.Now);

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            EnsureNotDuplicate(items, item.Title, item.Content);
            items.Insert(0, item);
            SaveUnsafe(items);
        }

        return item;
    }

    public KnowledgeItem UpdateReviewed(
        string id,
        string title,
        string content,
        string accountScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ValidateFields(title, content, accountScope);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("知识条目不存在。");
            }

            EnsureNotDuplicate(items, title, content, id);
            var updated = items[index] with
            {
                Title = title.Trim(),
                Content = content.Trim(),
                AccountScope = string.IsNullOrWhiteSpace(accountScope) ? "全部账号" : accountScope.Trim(),
                IsReviewed = true,
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var removed = items.RemoveAll(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (removed == 0)
            {
                throw new InvalidOperationException("知识条目不存在。");
            }

            SaveUnsafe(items);
        }
    }

    public string ExportJson()
    {
        lock (_sync)
        {
            return JsonSerializer.Serialize(LoadUnsafe(), JsonOptions);
        }
    }

    public int ImportReviewed(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        KnowledgeItem[] incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<KnowledgeItem[]>(json, JsonOptions)
                ?? throw new InvalidOperationException("导入文件不包含知识条目。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("知识文件不是有效 JSON。", exception);
        }

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var added = 0;
            foreach (var candidate in incoming)
            {
                ValidateFields(candidate.Title, candidate.Content, candidate.AccountScope);
                if (!candidate.IsReviewed
                    || items.Any(item => IsDuplicate(item, candidate.Title, candidate.Content)))
                {
                    continue;
                }

                items.Insert(0, candidate with
                {
                    Id = $"kb:local:{Guid.NewGuid():N}",
                    Title = candidate.Title.Trim(),
                    Content = candidate.Content.Trim(),
                    AccountScope = string.IsNullOrWhiteSpace(candidate.AccountScope)
                        ? "全部账号"
                        : candidate.AccountScope.Trim(),
                    IsReviewed = true,
                    UpdatedAt = DateTimeOffset.Now
                });
                added++;
            }

            if (added > 0)
            {
                SaveUnsafe(items);
            }

            return added;
        }
    }

    public KnowledgeItem ToggleEnabled(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("知识条目不存在。");
            }

            var updated = items[index] with
            {
                IsEnabled = !items[index].IsEnabled,
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public string ComputeFingerprint()
    {
        lock (_sync)
        {
            var material = string.Join('|', LoadUnsafe()
                .Where(item => item.IsReviewed && item.IsEnabled)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => $"{item.Id}:{item.Title}:{item.Content}:{item.AccountScope}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }
    }

    public Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string query,
        string accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terms = BuildTerms(query);
        if (terms.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeItem>>([]);
        }

        IReadOnlyList<KnowledgeItem> result;
        lock (_sync)
        {
            result = LoadUnsafe()
                .Where(item => item.IsReviewed && item.IsEnabled)
                .Where(item => item.AccountScope.Equals("全部账号", StringComparison.OrdinalIgnoreCase)
                    || item.AccountScope.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                    || accountId.Contains(item.AccountScope, StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    Item = item,
                    Score = terms.Count(term =>
                        item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || item.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Item.UpdatedAt)
                .Take(6)
                .Select(candidate => candidate.Item)
                .ToArray();
        }

        return Task.FromResult(result);
    }

    private IReadOnlyList<KnowledgeItem> LoadUnsafe()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<KnowledgeItem[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void SaveUnsafe(IReadOnlyList<KnowledgeItem> items)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("知识库路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temporaryPath, _filePath, true);
    }

    private static void ValidateFields(string title, string content, string accountScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (title.Trim().Length is < 2 or > 100)
        {
            throw new InvalidOperationException("知识标题需为 2–100 个字符。");
        }

        if (content.Trim().Length is < 2 or > 4000)
        {
            throw new InvalidOperationException("知识正文需为 2–4000 个字符。");
        }

        if (!string.IsNullOrWhiteSpace(accountScope) && accountScope.Trim().Length > 80)
        {
            throw new InvalidOperationException("适用账号不能超过 80 个字符。");
        }
    }

    private static void EnsureNotDuplicate(
        IEnumerable<KnowledgeItem> items,
        string title,
        string content,
        string? exceptId = null)
    {
        if (items.Any(item => !item.Id.Equals(exceptId, StringComparison.Ordinal)
            && IsDuplicate(item, title, content)))
        {
            throw new InvalidOperationException("相同标题和正文的知识已经存在。");
        }
    }

    private static bool IsDuplicate(KnowledgeItem item, string title, string content) =>
        item.Title.Trim().Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)
        && item.Content.Trim().Equals(content.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> BuildTerms(string query)
    {
        var normalized = new string(query
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in query.Split(
                     [' ', ',', '，', '。', '?', '？', '!', '！', ':', '：', ';', '；'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Length >= 2)
            {
                terms.Add(word);
            }
        }

        for (var index = 0; index + 1 < normalized.Length; index++)
        {
            terms.Add(normalized.Substring(index, 2));
        }

        return terms;
    }
}
