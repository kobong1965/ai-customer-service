namespace AgentDesk.Core;

public enum AgentMode
{
    Stopped,
    Simulation,
    Live
}

public enum AgentExecutionMode
{
    Shadow,
    AutoSend
}

public enum AgentStage
{
    Stopped,
    Monitoring,
    MessageDetected,
    AccountValidated,
    ContextRead,
    Drafting,
    SafetyCheck,
    ShadowObserved,
    Sending,
    Completed,
    RateLimited,
    HumanRequired,
    Failed
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public sealed record IncomingMessage(
    string Id,
    string AccountId,
    string CustomerAlias,
    string Text,
    DateTimeOffset ReceivedAt,
    string? ScreenshotDataUrl = null,
    string ProductKey = "");

public enum ScreenAction
{
    None,
    SwitchAccount,
    OpenConversation,
    ProcessActiveConversation
}

public sealed record ScreenObservation(
    ScreenAction Action,
    double ClickX,
    double ClickY,
    double Confidence,
    string CustomerAlias,
    string LatestCustomerMessage,
    string Summary,
    string AccountLabel = "",
    string ProductKey = "");

public sealed record RuntimeSafetyLimits(
    int DailySendLimit,
    int PerMinuteSendLimit)
{
    public static RuntimeSafetyLimits Default { get; } = new(100, 6);

    public bool IsValid => DailySendLimit is >= 1 and <= 10000
        && PerMinuteSendLimit is >= 1 and <= 120;
}

public sealed record ReplyDecision(
    RiskLevel RiskLevel,
    bool RequiresHuman,
    string DraftReply,
    IReadOnlyList<string> FactsUsed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string>? SkillsUsed = null);

public sealed record SendReceipt(
    string MessageId,
    string PlatformReference,
    DateTimeOffset SentAt);

public sealed record ProcessResult(
    IncomingMessage Incoming,
    ReplyDecision Decision,
    bool WasSent,
    string Outcome,
    SendReceipt? Receipt);

public sealed record SimulationCase(
    string Id,
    string Name,
    string CustomerMessage,
    bool ExpectAutoSend,
    string ExpectedOutcomeContains);

public sealed record SimulationCaseResult(
    string CaseId,
    string Name,
    bool Passed,
    bool WasSent,
    string Outcome,
    string ReplyText);

public sealed record SimulationApproval(
    string ConfigurationFingerprint,
    string OperatorName,
    DateTimeOffset ApprovedAt,
    IReadOnlyList<string> PassedCaseIds);

public sealed record RunEvent(
    DateTimeOffset Timestamp,
    string AccountId,
    AgentStage Stage,
    string Summary,
    bool IsError = false);

public sealed record ModelConnectionResult(
    bool Success,
    string Message,
    TimeSpan Latency);

public sealed record KnowledgeItem(
    string Id,
    string Title,
    string Content,
    string AccountScope,
    bool IsReviewed,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

public sealed record SizeRecommendationRow(
    string Size,
    double? MinHeightCm,
    double? MaxHeightCm,
    double? MinWeightKg,
    double? MaxWeightKg,
    double? MinWaistCm,
    double? MaxWaistCm,
    double? MinBustCm,
    double? MaxBustCm,
    string Notes);

public sealed record ProductSizingProfile(
    string Id,
    string ProductUrl,
    string ProductKey,
    string Category,
    string Fit,
    string Variant,
    string AccountScope,
    string MeasurementGuide,
    IReadOnlyList<SizeRecommendationRow> Rows,
    bool IsReviewed,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

public sealed record CustomerMeasurements(
    double? HeightCm,
    double? WeightKg,
    double? WaistCm,
    double? BustCm);

public enum SizingMatchStatus
{
    Matched,
    MissingMeasurements,
    NoMatch,
    MultipleMatches
}

public sealed record SizingMatchResult(
    SizingMatchStatus Status,
    SizeRecommendationRow? Row,
    string Message);

public enum MemoryReviewStatus
{
    Candidate,
    Approved
}

public sealed record ExperienceMemory(
    string Id,
    string Title,
    string Content,
    string Tags,
    string AccountScope,
    string ProductKey,
    MemoryReviewStatus ReviewStatus,
    bool IsEnabled,
    double Confidence,
    int EvidenceCount,
    int UsageCount,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt = null);

public sealed record AgentSkill(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> TriggerTerms,
    string Instructions,
    string SourceUrl,
    string License,
    bool AlwaysApply,
    bool IsReviewed,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);
