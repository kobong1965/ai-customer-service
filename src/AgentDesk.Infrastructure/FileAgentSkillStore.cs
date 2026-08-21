using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileAgentSkillStore : IAgentSkillProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] ForbiddenInstructionFragments =
    [
        "powershell", "cmd.exe", "bash ", "composio execute", "curl ", "wget ",
        "忽略系统", "绕过安全", "删除文件", "发送邮件"
    ];

    private readonly object _sync = new();
    private readonly string _filePath;

    public FileAgentSkillStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "skills.json");
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                SaveUnsafe(RecommendedSkills);
            }
        }
    }

    public static IReadOnlyList<AgentSkill> RecommendedSkills { get; } =
    [
        BuiltIn(
            "response-quality-review",
            "回复质量复核",
            "发送前检查是否直接回答、有依据、无过度承诺。",
            "质量与安全",
            [],
            "先回答客户当前最新问题；删掉无关铺垫和重复句；对库存、价格、时效、尺码等事实只使用已提供的可验证依据；涉及退款、赔偿、投诉、改价、改地址或资料冲突时标记转人工。",
            true),
        BuiltIn(
            "concise-friendly-tone",
            "简洁友好语气",
            "将回复整理为简短、自然、不生硬的中文客服表达。",
            "语气",
            [],
            "优先使用一到三句口语化中文；先给明确结论，再补充必要的操作或追问；不堆叠道歉、不使用夸张承诺，不暴露模型、规则、记忆或技能名称。",
            true),
        BuiltIn(
            "missing-info-clarifier",
            "缺失信息追问",
            "资料不足时只追问作决策必需的最少信息。",
            "流程",
            ["尺码", "怎么", "什么", "有货", "发货", "物流", "订单", "颜色", "面料"],
            "当前信息不能安全得出结论时，不猜测；一次最多追问两项决定性信息。尺码问题优先确认商品版本、身高、体重与品类必需围度；物流问题优先核对可见订单状态。若无法从界面核实，转人工。",
            false),
        BuiltIn(
            "sentiment-urgency",
            "情绪与紧急度识别",
            "识别不满、焦急、投诉和时效压力，降低自动处理风险。",
            "风险",
            ["生气", "投诉", "差评", "着急", "马上", "立刻", "退款", "赔偿"],
            "先识别客户是否在表达不满、反复催促或已经升级为投诉；只承认已发生的感受和问题，不承诺无法确认的结果。出现赔偿、投诉、差评、退款争议或对方要求立即承诺时，必须转人工。",
            false),
        BuiltIn(
            "angry-customer-deescalation",
            "激动客户降级处理",
            "在高情绪对话中先稳定沟通，再给出可核实的下一步。",
            "风险",
            ["生气", "气死", "太差", "垃圾", "投诉", "差评", "骗人"],
            "用一句话确认客户的具体问题，避免教训、反驳或连续道歉；只给出当前可执行且有依据的下一步。不得自行承诺退款、赔偿、改价或处理时限；达到这些情形时标记转人工。",
            false)
    ];

    public IReadOnlyList<AgentSkill> LoadAll()
    {
        lock (_sync) return LoadUnsafe();
    }

    public AgentSkill AddReviewed(string name, string description, string category, string triggerTerms,
        string instructions, string sourceUrl, string license, bool alwaysApply)
    {
        Validate(name, description, category, triggerTerms, instructions, sourceUrl, license);
        var skill = new AgentSkill(
            $"skill:local:{Guid.NewGuid():N}", name.Trim(), description.Trim(), NormalizeCategory(category),
            NormalizeTriggers(triggerTerms), instructions.Trim(), sourceUrl.Trim(), license.Trim(), alwaysApply,
            true, true, DateTimeOffset.Now);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            EnsureUniqueName(items, skill.Name);
            items.Insert(0, skill);
            SaveUnsafe(items);
        }
        return skill;
    }

    public AgentSkill UpdateReviewed(string id, string name, string description, string category,
        string triggerTerms, string instructions, string sourceUrl, string license, bool alwaysApply)
    {
        Validate(name, description, category, triggerTerms, instructions, sourceUrl, license);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = Find(items, id);
            EnsureUniqueName(items, name, id);
            var updated = items[index] with
            {
                Name = name.Trim(),
                Description = description.Trim(),
                Category = NormalizeCategory(category),
                TriggerTerms = NormalizeTriggers(triggerTerms),
                Instructions = instructions.Trim(),
                SourceUrl = sourceUrl.Trim(),
                License = license.Trim(),
                AlwaysApply = alwaysApply,
                IsReviewed = true,
                IsEnabled = true,
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public AgentSkill Approve(string id)
    {
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = Find(items, id);
            var updated = items[index] with { IsReviewed = true, IsEnabled = true, UpdatedAt = DateTimeOffset.Now };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public AgentSkill ToggleEnabled(string id)
    {
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = Find(items, id);
            if (!items[index].IsReviewed) throw new InvalidOperationException("导入技能必须先人工审核。");
            var updated = items[index] with { IsEnabled = !items[index].IsEnabled, UpdatedAt = DateTimeOffset.Now };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public void Delete(string id)
    {
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            if (items.RemoveAll(item => item.Id.Equals(id, StringComparison.Ordinal)) == 0)
                throw new InvalidOperationException("技能不存在。");
            SaveUnsafe(items);
        }
    }

    public int RestoreRecommended()
    {
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var missing = RecommendedSkills.Where(skill => items.All(item => item.Id != skill.Id)).ToArray();
            if (missing.Length > 0)
            {
                items.InsertRange(0, missing.Select(skill => skill with { UpdatedAt = DateTimeOffset.Now }));
                SaveUnsafe(items);
            }
            return missing.Length;
        }
    }

    public int ImportForReview(string json)
    {
        AgentSkill?[] incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<AgentSkill?[]>(json, JsonOptions)
                ?? throw new InvalidOperationException("导入文件不包含技能。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("技能文件不是有效 JSON。", exception);
        }

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var added = 0;
            foreach (var item in incoming.Where(item => item is not null).Cast<AgentSkill>())
            {
                var triggers = string.Join(',', item.TriggerTerms ?? []);
                try { Validate(item.Name, item.Description, item.Category, triggers, item.Instructions, item.SourceUrl, item.License); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { continue; }
                if (items.Any(existing => existing.Name.Equals(item.Name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                items.Insert(0, item with
                {
                    Id = $"skill:import:{Guid.NewGuid():N}",
                    Name = item.Name.Trim(),
                    Description = item.Description.Trim(),
                    Category = NormalizeCategory(item.Category),
                    TriggerTerms = NormalizeTriggers(triggers),
                    Instructions = item.Instructions.Trim(),
                    SourceUrl = item.SourceUrl.Trim(),
                    License = item.License.Trim(),
                    IsReviewed = false,
                    IsEnabled = false,
                    UpdatedAt = DateTimeOffset.Now
                });
                added++;
            }
            if (added > 0) SaveUnsafe(items);
            return added;
        }
    }

    public string ExportJson()
    {
        lock (_sync) return JsonSerializer.Serialize(LoadUnsafe(), JsonOptions);
    }

    public Task<IReadOnlyList<AgentSkill>> MatchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = LoadAll()
            .Where(item => item.IsReviewed && item.IsEnabled)
            .Where(item => item.AlwaysApply || Matches(item, query))
            .OrderByDescending(item => item.AlwaysApply)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AgentSkill>>(result);
    }

    public string ComputeFingerprint()
    {
        var material = string.Join('|', LoadAll().Where(item => item.IsReviewed && item.IsEnabled)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => $"{item.Id}:{item.Name}:{item.Description}:{item.Category}:{string.Join(',', item.TriggerTerms)}:{item.Instructions}:{item.AlwaysApply}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private IReadOnlyList<AgentSkill> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_filePath)) return [];
            return (JsonSerializer.Deserialize<AgentSkill?[]>(File.ReadAllText(_filePath), JsonOptions) ?? [])
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Instructions))
                .Cast<AgentSkill>().ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException) { return []; }
    }

    private void SaveUnsafe(IReadOnlyList<AgentSkill> items)
    {
        var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("技能存储路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temporaryPath, _filePath, true);
    }

    private static AgentSkill BuiltIn(string key, string name, string description, string category,
        IReadOnlyList<string> triggers, string instructions, bool alwaysApply) => new(
        $"skill:built-in:{key}", name, description, category, triggers, instructions,
        "https://github.com/composio-community/support-skills", "MIT（仓库 README 声明）",
        alwaysApply, true, true, DateTimeOffset.Now);

    private static int Find(IReadOnlyList<AgentSkill> items, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        for (var index = 0; index < items.Count; index++) if (items[index].Id == id) return index;
        throw new InvalidOperationException("技能不存在。");
    }

    private static bool Matches(AgentSkill item, string query) =>
        item.TriggerTerms.Any(term => query.Contains(term, StringComparison.OrdinalIgnoreCase))
        || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCategory(string? value) => string.IsNullOrWhiteSpace(value) ? "自定义" : value.Trim();

    private static string[] NormalizeTriggers(string? value) => (value ?? string.Empty)
        .Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();

    private static void EnsureUniqueName(IEnumerable<AgentSkill> items, string name, string? exceptId = null)
    {
        if (items.Any(item => item.Id != exceptId && item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已存在同名技能。");
    }

    private static void Validate(string name, string description, string category, string triggers,
        string instructions, string sourceUrl, string license)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        if (name.Trim().Length is < 2 or > 64) throw new InvalidOperationException("技能名需为 2–64 个字符。");
        if (description.Trim().Length is < 4 or > 500) throw new InvalidOperationException("技能说明需为 4–500 个字符。");
        if (instructions.Trim().Length is < 20 or > 8000) throw new InvalidOperationException("技能指令需为 20–8000 个字符。");
        if ((category?.Trim().Length ?? 0) > 50 || (license?.Trim().Length ?? 0) > 80)
            throw new InvalidOperationException("技能分类或授权信息过长。");
        var normalizedTriggers = NormalizeTriggers(triggers);
        if (normalizedTriggers.Any(value => value.Length > 40)) throw new InvalidOperationException("单个触发词不能超过 40 个字符。");
        if (!string.IsNullOrWhiteSpace(sourceUrl)
            && (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            throw new InvalidOperationException("来源链接必须是 HTTP/HTTPS 地址。");
        if (ForbiddenInstructionFragments.Any(fragment => instructions.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("技能包含命令执行、外部写入或绕过安全的高风险指令。");
    }
}
