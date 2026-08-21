using AgentDesk.Core;

namespace AgentDesk.Core.Tests;

public sealed class AgentOrchestratorTests
{
    [Fact]
    public async Task LowRiskMessage_IsAutomaticallySent_WhenAllowed()
    {
        var platform = new FakePlatform();
        platform.Enqueue("有货吗？");
        var generator = new FakeReplyGenerator(new ReplyDecision(
            RiskLevel.Low,
            false,
            "有货，可以下单。",
            ["kb:test"],
            []));
        var orchestrator = new AgentOrchestrator(platform, generator);

        var result = await orchestrator.ProcessNextAsync(true);

        Assert.NotNull(result);
        Assert.True(result.WasSent);
        Assert.Single(platform.SentReplies);
        Assert.Contains("自动发送", result.Outcome);
    }

    [Fact]
    public async Task HighRiskMessage_IsNotSent_EvenWhenAutoSendIsAllowed()
    {
        var platform = new FakePlatform();
        platform.Enqueue("我要退款");
        var generator = new FakeReplyGenerator(new ReplyDecision(
            RiskLevel.High,
            true,
            string.Empty,
            [],
            ["退款转人工"]));
        var orchestrator = new AgentOrchestrator(platform, generator);

        var result = await orchestrator.ProcessNextAsync(true);

        Assert.NotNull(result);
        Assert.False(result.WasSent);
        Assert.Empty(platform.SentReplies);
        Assert.Contains("转人工", result.Outcome);
    }

    [Fact]
    public async Task LowRiskMessage_IsNotSent_WhenAutoSendIsLocked()
    {
        var platform = new FakePlatform();
        platform.Enqueue("有货吗？");
        var generator = new FakeReplyGenerator(new ReplyDecision(
            RiskLevel.Low,
            false,
            "有货，可以下单。",
            ["kb:test"],
            []));
        var orchestrator = new AgentOrchestrator(platform, generator);

        var result = await orchestrator.ProcessNextAsync(false);

        Assert.NotNull(result);
        Assert.False(result.WasSent);
        Assert.Empty(platform.SentReplies);
        Assert.Contains("尚未解锁", result.Outcome);
    }

    [Fact]
    public async Task LowRiskMessage_IsNotSent_WhenGroundingFactsAreMissing()
    {
        var platform = new FakePlatform();
        platform.Enqueue("有货吗？");
        var generator = new FakeReplyGenerator(new ReplyDecision(
            RiskLevel.Low,
            false,
            "有货。",
            [],
            []));
        var orchestrator = new AgentOrchestrator(platform, generator);

        var result = await orchestrator.ProcessNextAsync(true);

        Assert.NotNull(result);
        Assert.False(result.WasSent);
        Assert.Empty(platform.SentReplies);
        Assert.Contains("转人工", result.Outcome);
    }

    [Fact]
    public async Task CompletedDecision_IsOfferedToExperienceRecorder()
    {
        var platform = new FakePlatform();
        platform.Enqueue("有货吗？");
        var recorder = new FakeExperienceRecorder();
        var orchestrator = new AgentOrchestrator(platform, new FakeReplyGenerator(new ReplyDecision(
            RiskLevel.Low, false, "有货。", ["kb:test"], [])), recorder);

        var result = await orchestrator.ProcessNextAsync(true);

        Assert.Same(result, recorder.LastResult);
    }

    private sealed class FakeReplyGenerator(ReplyDecision decision) : IReplyGenerator
    {
        public Task<ReplyDecision> GenerateAsync(
            IncomingMessage incoming,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class FakeExperienceRecorder : IExperienceRecorder
    {
        public ProcessResult? LastResult { get; private set; }
        public Task RecordAsync(ProcessResult result, CancellationToken cancellationToken)
        {
            LastResult = result;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlatform : ISupportPlatformAdapter
    {
        private readonly Queue<IncomingMessage> _incoming = new();

        public string Name => "Fake";
        public List<string> SentReplies { get; } = [];

        public void Enqueue(string text) => _incoming.Enqueue(new IncomingMessage(
            Guid.NewGuid().ToString("N"),
            "account",
            "customer",
            text,
            DateTimeOffset.Now));

        public ValueTask<IncomingMessage?> ReceiveNextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_incoming.Count > 0 ? _incoming.Dequeue() : null);

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
