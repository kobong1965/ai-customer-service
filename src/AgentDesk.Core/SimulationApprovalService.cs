namespace AgentDesk.Core;

public sealed class SimulationApprovalService(ISimulationApprovalStore store)
{
    public static readonly IReadOnlySet<string> RequiredCaseIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "stock-low-risk",
        "shipping-low-risk",
        "address-high-risk",
        "complaint-high-risk",
        "unknown-no-facts"
    };

    public bool CanApprove(IReadOnlyCollection<SimulationCaseResult> results)
    {
        var passedIds = results
            .Where(result => result.Passed)
            .Select(result => result.CaseId)
            .ToHashSet(StringComparer.Ordinal);

        return RequiredCaseIds.IsSubsetOf(passedIds);
    }

    public async Task<SimulationApproval> ApproveAsync(
        IReadOnlyCollection<SimulationCaseResult> results,
        bool humanConfirmed,
        string configurationFingerprint,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        if (!humanConfirmed)
        {
            throw new InvalidOperationException("必须由人工确认已检查模拟发送结果。");
        }

        if (!CanApprove(results))
        {
            throw new InvalidOperationException("必测用例尚未全部通过，不能解锁正式模式。");
        }

        var approval = new SimulationApproval(
            configurationFingerprint,
            string.IsNullOrWhiteSpace(operatorName) ? "本机管理员" : operatorName.Trim(),
            DateTimeOffset.Now,
            results.Where(result => result.Passed).Select(result => result.CaseId).Distinct().ToArray());

        await store.SaveAsync(approval, cancellationToken);
        return approval;
    }

    public async Task<bool> IsLiveUnlockedAsync(
        string configurationFingerprint,
        CancellationToken cancellationToken = default)
    {
        var approval = await store.LoadAsync(cancellationToken);
        return approval is not null
            && string.Equals(
                approval.ConfigurationFingerprint,
                configurationFingerprint,
                StringComparison.Ordinal)
            && RequiredCaseIds.IsSubsetOf(approval.PassedCaseIds.ToHashSet(StringComparer.Ordinal));
    }

    public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        store.ClearAsync(cancellationToken);
}
