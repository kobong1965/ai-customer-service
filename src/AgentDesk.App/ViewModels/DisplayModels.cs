using AgentDesk.Core;

namespace AgentDesk.App.ViewModels;

public sealed record SimulationTranscriptItem(
    string Sender,
    string Text,
    string Status,
    bool IsAgent);

public sealed record AccountDisplayItem(
    string Badge,
    string Name,
    string AccountId,
    string Status,
    int TodayCount);

public sealed class SimulationCaseResultItem(SimulationCaseResult result)
{
    public string CaseId { get; } = result.CaseId;
    public string Name { get; } = result.Name;
    public bool Passed { get; } = result.Passed;
    public string Status { get; } = result.Passed ? "通过" : "失败";
    public string SendStatus { get; } = result.WasSent ? "已自动发送" : "已安全拦截";
    public string Outcome { get; } = result.Outcome;
    public SimulationCaseResult Source { get; } = result;
}

public sealed class KnowledgeDisplayItem(KnowledgeItem source)
{
    public string Id { get; } = source.Id;
    public string Title { get; } = source.Title;
    public string Content { get; } = source.Content;
    public string AccountScope { get; } = source.AccountScope;
    public string Status { get; } = source.IsEnabled ? "已审核 · 已启用" : "已审核 · 已停用";
    public string ToggleText { get; } = source.IsEnabled ? "停用" : "重新启用";
    public bool IsEnabled { get; } = source.IsEnabled;
    public KnowledgeItem Source { get; } = source;
}

public sealed class MemoryDisplayItem(ExperienceMemory source)
{
    public string Id { get; } = source.Id;
    public string Title { get; } = source.Title;
    public string Content { get; } = source.Content;
    public string Tags { get; } = source.Tags;
    public string ScopeText { get; } = string.IsNullOrWhiteSpace(source.ProductKey)
        ? source.AccountScope
        : $"{source.AccountScope} · {source.ProductKey}";
    public string Status { get; } = source.ReviewStatus is MemoryReviewStatus.Candidate
        ? "待人工审核"
        : source.IsEnabled ? "长期记忆 · 已启用" : "长期记忆 · 已停用";
    public string EvidenceText { get; } = $"依据 {source.EvidenceCount} 次 · 使用 {source.UsageCount} 次 · 来源 {source.Source}";
    public string ToggleText { get; } = source.IsEnabled ? "停用" : "启用";
    public bool CanApprove { get; } = source.ReviewStatus is MemoryReviewStatus.Candidate;
    public bool CanToggle { get; } = source.ReviewStatus is MemoryReviewStatus.Approved;
    public ExperienceMemory Source { get; } = source;
}

public sealed class AgentSkillDisplayItem(AgentSkill source)
{
    public string Id { get; } = source.Id;
    public string Name { get; } = source.Name;
    public string Description { get; } = source.Description;
    public string Category { get; } = source.Category;
    public string TriggerText { get; } = source.AlwaysApply
        ? "每次适用"
        : source.TriggerTerms.Count == 0 ? "无触发词" : string.Join("、", source.TriggerTerms);
    public string SourceText { get; } = string.IsNullOrWhiteSpace(source.SourceUrl)
        ? $"本地 · {source.License}"
        : $"{source.License} · {source.SourceUrl}";
    public string Status { get; } = !source.IsReviewed
        ? "待人工审核"
        : source.IsEnabled ? "已审核 · 已启用" : "已审核 · 已停用";
    public string ToggleText { get; } = source.IsEnabled ? "停用" : "启用";
    public bool CanApprove { get; } = !source.IsReviewed;
    public bool CanToggle { get; } = source.IsReviewed;
    public AgentSkill Source { get; } = source;
}

public sealed class ProductSizingDisplayItem(ProductSizingProfile source)
{
    public string Id { get; } = source.Id;
    public string ProductKey { get; } = source.ProductKey;
    public string ProductUrl { get; } = source.ProductUrl;
    public string Classification { get; } = $"{source.Category} / {source.Fit} / {source.Variant}";
    public string AccountScope { get; } = source.AccountScope;
    public string RowSummary { get; } = string.Join(" · ", source.Rows.Select(row => row.Size));
    public string Status { get; } = source.IsEnabled ? "已审核 · 已启用" : "已审核 · 已停用";
    public string ToggleText { get; } = source.IsEnabled ? "停用" : "重新启用";
    public ProductSizingProfile Source { get; } = source;
}

public sealed class SizingRowEditor : ObservableObject
{
    private string _size = string.Empty;
    private double? _minHeightCm;
    private double? _maxHeightCm;
    private double? _minWeightKg;
    private double? _maxWeightKg;
    private double? _minWaistCm;
    private double? _maxWaistCm;
    private double? _minBustCm;
    private double? _maxBustCm;
    private string _notes = string.Empty;

    public SizingRowEditor()
    {
    }

    public SizingRowEditor(SizeRecommendationRow source)
    {
        _size = source.Size;
        _minHeightCm = source.MinHeightCm;
        _maxHeightCm = source.MaxHeightCm;
        _minWeightKg = source.MinWeightKg;
        _maxWeightKg = source.MaxWeightKg;
        _minWaistCm = source.MinWaistCm;
        _maxWaistCm = source.MaxWaistCm;
        _minBustCm = source.MinBustCm;
        _maxBustCm = source.MaxBustCm;
        _notes = source.Notes;
    }

    public string Size { get => _size; set => SetProperty(ref _size, value); }
    public double? MinHeightCm { get => _minHeightCm; set => SetProperty(ref _minHeightCm, value); }
    public double? MaxHeightCm { get => _maxHeightCm; set => SetProperty(ref _maxHeightCm, value); }
    public double? MinWeightKg { get => _minWeightKg; set => SetProperty(ref _minWeightKg, value); }
    public double? MaxWeightKg { get => _maxWeightKg; set => SetProperty(ref _maxWeightKg, value); }
    public double? MinWaistCm { get => _minWaistCm; set => SetProperty(ref _minWaistCm, value); }
    public double? MaxWaistCm { get => _maxWaistCm; set => SetProperty(ref _maxWaistCm, value); }
    public double? MinBustCm { get => _minBustCm; set => SetProperty(ref _minBustCm, value); }
    public double? MaxBustCm { get => _maxBustCm; set => SetProperty(ref _maxBustCm, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public SizeRecommendationRow ToModel() => new(
        Size,
        MinHeightCm,
        MaxHeightCm,
        MinWeightKg,
        MaxWeightKg,
        MinWaistCm,
        MaxWaistCm,
        MinBustCm,
        MaxBustCm,
        Notes);
}

public sealed class RunEventDisplayItem(RunEvent source)
{
    public DateTimeOffset Timestamp { get; } = source.Timestamp;
    public string TimeText { get; } = source.Timestamp.ToString("MM-dd HH:mm:ss");
    public string AccountId { get; } = string.IsNullOrWhiteSpace(source.AccountId) ? "系统" : source.AccountId;
    public string Stage { get; } = ToStageText(source.Stage);
    public string Summary { get; } = source.Summary;
    public bool IsError { get; } = source.IsError;
    public string SeverityText { get; } = source.IsError ? "错误" : "正常";
    public RunEvent Source { get; } = source;

    private static string ToStageText(AgentStage stage) => stage switch
    {
        AgentStage.Monitoring => "观察",
        AgentStage.MessageDetected => "新消息",
        AgentStage.Drafting => "生成决策",
        AgentStage.SafetyCheck => "安全检查",
        AgentStage.ShadowObserved => "影子结果",
        AgentStage.Sending => "发送中",
        AgentStage.Completed => "已发送",
        AgentStage.HumanRequired => "转人工",
        AgentStage.RateLimited => "触发限额",
        AgentStage.Stopped => "已停止",
        AgentStage.Failed => "失败",
        _ => stage.ToString()
    };
}

public sealed record ReadinessDisplayItem(
    string Number,
    string Title,
    string Description,
    string Status,
    bool IsReady,
    string TargetPage);

public sealed record ExecutionModeDisplayItem(
    AgentExecutionMode Value,
    string Name,
    string Description);
