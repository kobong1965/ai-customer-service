using AgentDesk.Core;

namespace AgentDesk.Core.Tests;

public sealed class AgentRuntimeServiceTests
{
    [Fact]
    public async Task ShadowMode_RecordsDecisionWithoutSending()
    {
        var platform = new FakePlatform();
        platform.Enqueue();
        var orchestrator = new AgentOrchestrator(platform, new LowRiskGenerator());
        var shadowEvent = new TaskCompletionSource<RunEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new AgentRuntimeService(
            orchestrator,
            TimeSpan.FromMilliseconds(10),
            AgentExecutionMode.Shadow);
        service.EventRecorded += (_, runEvent) =>
        {
            if (runEvent.Stage is AgentStage.ShadowObserved)
            {
                shadowEvent.TrySetResult(runEvent);
            }
        };

        await service.StartAsync();
        await shadowEvent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Empty(platform.SentReplies);
    }

    [Fact]
    public async Task AutoSend_StopsBeforePollingWhenDailyLimitAlreadyReached()
    {
        var platform = new FakePlatform();
        var orchestrator = new AgentOrchestrator(platform, new LowRiskGenerator());
        var stopped = new TaskCompletionSource<RunEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new AgentRuntimeService(
            orchestrator,
            TimeSpan.FromMilliseconds(10),
            AgentExecutionMode.AutoSend,
            new RuntimeSafetyLimits(1, 1),
            initialDailySentCount: 1);
        service.EventRecorded += (_, runEvent) =>
        {
            if (runEvent.Stage is AgentStage.Stopped && runEvent.IsError)
            {
                stopped.TrySetResult(runEvent);
            }
        };

        await service.StartAsync();
        var result = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("限额", result.Summary);
        Assert.Equal(0, platform.ReceiveCount);
    }

    private sealed class LowRiskGenerator : IReplyGenerator
    {
        public Task<ReplyDecision> GenerateAsync(
            IncomingMessage incoming,
            CancellationToken cancellationToken) => Task.FromResult(new ReplyDecision(
                RiskLevel.Low,
                false,
                "有货，可以下单。",
                ["kb:test"],
                []));
    }

    private sealed class FakePlatform : ISupportPlatformAdapter
    {
        private readonly Queue<IncomingMessage> _messages = new();

        public string Name => "Fake";
        public int ReceiveCount { get; private set; }
        public List<string> SentReplies { get; } = [];

        public void Enqueue() => _messages.Enqueue(new IncomingMessage(
            Guid.NewGuid().ToString("N"),
            "account",
            "customer",
            "有货吗？",
            DateTimeOffset.Now));

        public ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken)
        {
            ReceiveCount++;
            return ValueTask.FromResult(_messages.Count > 0 ? _messages.Dequeue() : null);
        }

        public Task<SendReceipt> SendReplyAsync(
            IncomingMessage incoming,
            string reply,
            CancellationToken cancellationToken)
        {
            SentReplies.Add(reply);
            return Task.FromResult(new SendReceipt(incoming.Id, "fake", DateTimeOffset.Now));
        }
    }
}
