using AgentDesk.Core;

namespace AgentDesk.Core.Tests;

public sealed class SimulationApprovalServiceTests
{
    [Fact]
    public async Task Approval_RequiresHumanConfirmation()
    {
        var service = new SimulationApprovalService(new MemoryStore());
        var results = PassingResults();

        var action = () => service.ApproveAsync(results, false, "fingerprint", "tester");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("人工确认", exception.Message);
    }

    [Fact]
    public async Task Approval_RequiresAllMandatoryCases()
    {
        var service = new SimulationApprovalService(new MemoryStore());
        var results = PassingResults().Skip(1).ToArray();

        var action = () => service.ApproveAsync(results, true, "fingerprint", "tester");

        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Fact]
    public async Task LiveUnlock_RequiresMatchingConfigurationFingerprint()
    {
        var store = new MemoryStore();
        var service = new SimulationApprovalService(store);
        await service.ApproveAsync(PassingResults(), true, "fingerprint-a", "tester");

        Assert.True(await service.IsLiveUnlockedAsync("fingerprint-a"));
        Assert.False(await service.IsLiveUnlockedAsync("fingerprint-b"));
    }

    private static SimulationCaseResult[] PassingResults() =>
        SimulationApprovalService.RequiredCaseIds
            .Select(id => new SimulationCaseResult(id, id, true, true, "通过", "reply"))
            .ToArray();

    private sealed class MemoryStore : ISimulationApprovalStore
    {
        private SimulationApproval? _approval;

        public Task<SimulationApproval?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_approval);

        public Task SaveAsync(SimulationApproval approval, CancellationToken cancellationToken)
        {
            _approval = approval;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _approval = null;
            return Task.CompletedTask;
        }
    }
}
