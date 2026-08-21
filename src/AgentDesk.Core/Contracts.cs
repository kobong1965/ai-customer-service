namespace AgentDesk.Core;

public interface ISupportPlatformAdapter
{
    string Name { get; }

    ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken);

    Task<SendReceipt> SendReplyAsync(
        IncomingMessage incoming,
        string reply,
        CancellationToken cancellationToken);
}

public interface IReplyGenerator
{
    Task<ReplyDecision> GenerateAsync(
        IncomingMessage incoming,
        CancellationToken cancellationToken);
}

public interface IModelConnectionTester
{
    Task<ModelConnectionResult> TestConnectionAsync(CancellationToken cancellationToken);
}

public interface IScreenObserver
{
    Task<ScreenObservation> ObserveAsync(
        string screenshotDataUrl,
        CancellationToken cancellationToken);
}

public interface IKnowledgeProvider
{
    Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string query,
        string accountId,
        CancellationToken cancellationToken);
}

public interface IProductSizingProvider
{
    Task<IReadOnlyList<ProductSizingProfile>> FindAsync(
        string productKey,
        string query,
        string accountId,
        CancellationToken cancellationToken);
}

public interface IExperienceMemoryProvider
{
    Task<IReadOnlyList<ExperienceMemory>> SearchApprovedAsync(
        string query,
        string accountId,
        string productKey,
        CancellationToken cancellationToken);
}

public interface IAgentSkillProvider
{
    Task<IReadOnlyList<AgentSkill>> MatchAsync(
        string query,
        CancellationToken cancellationToken);
}

public interface IExperienceRecorder
{
    Task RecordAsync(ProcessResult result, CancellationToken cancellationToken);
}

public interface ISimulationApprovalStore
{
    Task<SimulationApproval?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SimulationApproval approval, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
