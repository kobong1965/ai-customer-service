using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileExperienceMemoryStore : IExperienceMemoryProvider, IExperienceRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _sync = new();
    private readonly string _filePath;

    public FileExperienceMemoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "memories.json");
    }

    public bool AutoCaptureEnabled { get; set; } = true;
    public int CandidateLimit { get; set; } = 500;

    public IReadOnlyList<ExperienceMemory> LoadAll()
    {
        lock (_sync)
        {
            return LoadUnsafe();
        }
    }

    public ExperienceMemory AddCandidate(
        string title,
        string content,
        string tags,
        string accountScope,
        string productKey,
        string source = "manual")
    {
        Validate(title, content, tags, accountScope, productKey);
        var now = DateTimeOffset.Now;
        var memory = new ExperienceMemory(
            $"memory:local:{Guid.NewGuid():N}",
            title.Trim(),
            content.Trim(),
            NormalizeTags(tags),
            NormalizeScope(accountScope),
            productKey?.Trim() ?? string.Empty,
            MemoryReviewStatus.Candidate,
            false,
            0.5,
            1,
            0,
            string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim(),
            now,
            now);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            EnsureNotDuplicate(items, memory.Title, memory.Content);
            items.Insert(0, memory);
            SaveUnsafe(Prune(items));
        }

        return memory;
    }

    public ExperienceMemory UpdateAsCandidate(
        string id,
        string title,
        string content,
        string tags,
        string accountScope,
        string productKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Validate(title, content, tags, accountScope, productKey);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("记忆条目不存在。");
            }

            EnsureNotDuplicate(items, title, content, id);
            var updated = items[index] with
            {
                Title = title.Trim(),
                Content = content.Trim(),
                Tags = NormalizeTags(tags),
                AccountScope = NormalizeScope(accountScope),
                ProductKey = productKey?.Trim() ?? string.Empty,
                ReviewStatus = MemoryReviewStatus.Candidate,
                IsEnabled = false,
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public ExperienceMemory Approve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("记忆条目不存在。");
            }

            var updated = items[index] with
            {
                ReviewStatus = MemoryReviewStatus.Approved,
                IsEnabled = true,
                Confidence = Math.Max(items[index].Confidence, 0.8),
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public ExperienceMemory ToggleEnabled(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("记忆条目不存在。");
            }

            if (items[index].ReviewStatus is not MemoryReviewStatus.Approved)
            {
                throw new InvalidOperationException("候选记忆必须先人工审核批准。");
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

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            if (items.RemoveAll(item => item.Id.Equals(id, StringComparison.Ordinal)) == 0)
            {
                throw new InvalidOperationException("记忆条目不存在。");
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

    public int ImportAsCandidates(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ExperienceMemory?[] incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<ExperienceMemory?[]>(json, JsonOptions)
                ?? throw new InvalidOperationException("导入文件不包含记忆条目。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("记忆文件不是有效 JSON。", exception);
        }

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var added = 0;
            foreach (var item in incoming.Where(item => item is not null).Cast<ExperienceMemory>())
            {
                try
                {
                    Validate(item.Title, item.Content, item.Tags, item.AccountScope, item.ProductKey);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    continue;
                }

                if (items.Any(existing => IsDuplicate(existing, item.Title, item.Content)))
                {
                    continue;
                }

                var now = DateTimeOffset.Now;
                items.Insert(0, item with
                {
                    Id = $"memory:local:{Guid.NewGuid():N}",
                    Title = item.Title.Trim(),
                    Content = item.Content.Trim(),
                    Tags = NormalizeTags(item.Tags),
                    AccountScope = NormalizeScope(item.AccountScope),
                    ProductKey = item.ProductKey?.Trim() ?? string.Empty,
                    ReviewStatus = MemoryReviewStatus.Candidate,
                    IsEnabled = false,
                    Confidence = Math.Clamp(item.Confidence, 0, 0.7),
                    EvidenceCount = Math.Max(1, item.EvidenceCount),
                    UsageCount = 0,
                    Source = "import",
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastUsedAt = null
                });
                added++;
            }

            if (added > 0)
            {
                SaveUnsafe(Prune(items));
            }

            return added;
        }
    }

    public Task<IReadOnlyList<ExperienceMemory>> SearchApprovedAsync(
        string query,
        string accountId,
        string productKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terms = BuildTerms(query);
        IReadOnlyList<ExperienceMemory> result;
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var matches = items
                .Where(item => item.ReviewStatus is MemoryReviewStatus.Approved && item.IsEnabled)
                .Where(item => MatchesScope(item.AccountScope, accountId))
                .Where(item => string.IsNullOrWhiteSpace(item.ProductKey)
                    || item.ProductKey.Equals(productKey, StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    Item = item,
                    Score = terms.Count(term =>
                        item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || item.Content.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || item.Tags.Contains(term, StringComparison.OrdinalIgnoreCase))
                        + (item.ProductKey.Length > 0 ? 3 : 0)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Item.Confidence)
                .ThenByDescending(candidate => candidate.Item.UpdatedAt)
                .Take(6)
                .Select(candidate => candidate.Item)
                .ToArray();

            if (matches.Length > 0)
            {
                var now = DateTimeOffset.Now;
                var ids = matches.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                for (var index = 0; index < items.Count; index++)
                {
                    if (ids.Contains(items[index].Id))
                    {
                        items[index] = items[index] with
                        {
                            UsageCount = items[index].UsageCount + 1,
                            LastUsedAt = now
                        };
                    }
                }

                SaveUnsafe(items);
            }

            result = matches;
        }

        return Task.FromResult(result);
    }

    public Task RecordAsync(ProcessResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(result);
        if (!AutoCaptureEnabled
            || result.Incoming.AccountId.Equals("simulation-account", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var intent = ClassifyIntent(result.Incoming.Text);
        var outcome = result.WasSent
            ? "自动发送"
            : result.Decision.RequiresHuman ? "转人工" : "影子观察";
        var facts = StableIdentifiers(result.Decision.FactsUsed);
        var skills = (result.Decision.SkillsUsed ?? [])
            .Where(value => value.StartsWith("skill:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var product = string.IsNullOrWhiteSpace(result.Incoming.ProductKey)
            ? string.Empty
            : result.Incoming.ProductKey.Trim();
        var title = $"{intent}处理经验{(product.Length > 0 ? $" · {product}" : string.Empty)}";
        var content = $"处理类型：{intent}；风险：{result.Decision.RiskLevel}；结果：{outcome}；"
            + $"依据标识：{(facts.Length == 0 ? "仅屏幕可见事实" : string.Join("、", facts))}；"
            + $"使用技能：{(skills.Length == 0 ? "未记录" : string.Join("、", skills))}。"
            + "这是自动生成的脱敏候选，请人工核对并补充可复用做法后再批准。";
        var confidence = result.WasSent ? 0.55 : result.Decision.RequiresHuman ? 0.45 : 0.35;

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item =>
                item.ReviewStatus is MemoryReviewStatus.Candidate
                && item.Source.Equals("runtime", StringComparison.OrdinalIgnoreCase)
                && item.Title.Equals(title, StringComparison.OrdinalIgnoreCase)
                && item.AccountScope.Equals(result.Incoming.AccountId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                items[index] = items[index] with
                {
                    EvidenceCount = items[index].EvidenceCount + 1,
                    Confidence = Math.Min(0.75, items[index].Confidence + 0.03),
                    Content = content,
                    UpdatedAt = DateTimeOffset.Now
                };
            }
            else
            {
                var now = DateTimeOffset.Now;
                items.Insert(0, new ExperienceMemory(
                    $"memory:auto:{Guid.NewGuid():N}",
                    title,
                    content,
                    $"{intent},{outcome}",
                    result.Incoming.AccountId,
                    product,
                    MemoryReviewStatus.Candidate,
                    false,
                    confidence,
                    1,
                    0,
                    "runtime",
                    now,
                    now));
            }

            SaveUnsafe(Prune(items));
        }

        return Task.CompletedTask;
    }

    public string ComputeFingerprint()
    {
        lock (_sync)
        {
            var material = string.Join('|', LoadUnsafe()
                .Where(item => item.ReviewStatus is MemoryReviewStatus.Approved && item.IsEnabled)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => $"{item.Id}:{item.Title}:{item.Content}:{item.Tags}:{item.AccountScope}:{item.ProductKey}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }
    }

    private IReadOnlyList<ExperienceMemory> LoadUnsafe()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<ExperienceMemory?[]>(File.ReadAllText(_filePath), JsonOptions) ?? [];
            return items
                .Where(item => item is not null
                    && !string.IsNullOrWhiteSpace(item.Title)
                    && !string.IsNullOrWhiteSpace(item.Content))
                .Cast<ExperienceMemory>()
                .ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return [];
        }
    }

    private void SaveUnsafe(IReadOnlyList<ExperienceMemory> items)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("记忆体路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temporaryPath, _filePath, true);
    }

    private IReadOnlyList<ExperienceMemory> Prune(IReadOnlyList<ExperienceMemory> items)
    {
        var limit = Math.Clamp(CandidateLimit, 50, 5000);
        var approved = items.Where(item => item.ReviewStatus is MemoryReviewStatus.Approved);
        var candidates = items
            .Where(item => item.ReviewStatus is MemoryReviewStatus.Candidate)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(limit);
        return approved.Concat(candidates)
            .OrderByDescending(item => item.UpdatedAt)
            .ToArray();
    }

    private static string ClassifyIntent(string text)
    {
        if (ContainsAny(text, "尺码", "码数", "身高", "体重", "腰围", "胸围")) return "尺码";
        if (ContainsAny(text, "库存", "有货", "现货", "缺货")) return "库存";
        if (ContainsAny(text, "发货", "物流", "快递", "到货")) return "物流";
        if (ContainsAny(text, "退款", "退货", "赔偿", "投诉", "差评")) return "售后风险";
        if (ContainsAny(text, "材质", "面料", "颜色", "款式", "版型")) return "商品信息";
        return "一般售前";
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string[] StableIdentifiers(IReadOnlyList<string> facts) => facts
        .SelectMany(fact => Regex.Matches(
                fact,
                @"(?:kb|size|memory):[A-Za-z0-9:_-]+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .ToArray();

    private static string NormalizeTags(string? tags) => string.Join(',', (tags ?? string.Empty)
        .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(20));

    private static string NormalizeScope(string? accountScope) =>
        string.IsNullOrWhiteSpace(accountScope) ? "全部账号" : accountScope.Trim();

    private static bool MatchesScope(string scope, string accountId) =>
        scope.Equals("全部账号", StringComparison.OrdinalIgnoreCase)
        || scope.Equals(accountId, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(accountId) && accountId.Contains(scope, StringComparison.OrdinalIgnoreCase));

    private static void Validate(string title, string content, string? tags, string? accountScope, string? productKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (title.Trim().Length is < 2 or > 120) throw new InvalidOperationException("记忆标题需为 2–120 个字符。");
        if (content.Trim().Length is < 4 or > 4000) throw new InvalidOperationException("记忆内容需为 4–4000 个字符。");
        if ((tags?.Trim().Length ?? 0) > 300) throw new InvalidOperationException("记忆标签不能超过 300 个字符。");
        if ((accountScope?.Trim().Length ?? 0) > 80) throw new InvalidOperationException("适用账号不能超过 80 个字符。");
        if ((productKey?.Trim().Length ?? 0) > 200) throw new InvalidOperationException("商品标识不能超过 200 个字符。");
    }

    private static void EnsureNotDuplicate(
        IEnumerable<ExperienceMemory> items,
        string title,
        string content,
        string? exceptId = null)
    {
        if (items.Any(item => !item.Id.Equals(exceptId, StringComparison.Ordinal)
            && IsDuplicate(item, title, content)))
        {
            throw new InvalidOperationException("相同标题和内容的记忆已经存在。");
        }
    }

    private static bool IsDuplicate(ExperienceMemory item, string title, string content) =>
        item.Title.Trim().Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)
        && item.Content.Trim().Equals(content.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> BuildTerms(string query)
    {
        var normalized = new string(query.Where(char.IsLetterOrDigit).ToArray());
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in query.Split(
                     [' ', ',', '，', '。', '?', '？', '!', '！', ':', '：', ';', '；'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Length >= 2) terms.Add(word);
        }

        for (var index = 0; index + 1 < normalized.Length; index++) terms.Add(normalized.Substring(index, 2));
        return terms;
    }
}
