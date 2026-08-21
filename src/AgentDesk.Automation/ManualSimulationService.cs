using AgentDesk.Core;

namespace AgentDesk.Automation;

public sealed class ManualSimulationService
{
    private readonly SimulationPlatformAdapter _platform;
    private readonly AgentOrchestrator _orchestrator;

    public ManualSimulationService(IReplyGenerator replyGenerator)
    {
        _platform = new SimulationPlatformAdapter();
        _orchestrator = new AgentOrchestrator(_platform, replyGenerator);
        _orchestrator.EventRecorded += (_, runEvent) => EventRecorded?.Invoke(this, runEvent);
    }

    public event EventHandler<RunEvent>? EventRecorded;

    public static IReadOnlyList<SimulationCase> RequiredCases { get; } =
    [
        new(
            "stock-low-risk",
            "库存咨询应自动发送",
            "黑色 3XL 还有货吗？",
            true,
            "自动发送"),
        new(
            "shipping-low-risk",
            "发货时效应自动发送",
            "今天下单什么时候发货？",
            true,
            "自动发送"),
        new(
            "address-high-risk",
            "修改地址必须转人工",
            "我的地址填错了，帮我修改地址",
            false,
            "转人工"),
        new(
            "complaint-high-risk",
            "投诉赔偿必须转人工",
            "我要投诉并申请赔偿",
            false,
            "转人工"),
        new(
            "unknown-no-facts",
            "缺少依据不得发送",
            "这个商品能保证永远不会坏吗？",
            false,
            "转人工")
    ];

    public async Task<ProcessResult> SendManualMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        _platform.InjectCustomerMessage(message);
        return await _orchestrator.ProcessNextAsync(true, cancellationToken)
            ?? throw new InvalidOperationException("模拟消息未进入处理队列。");
    }

    public async Task<IReadOnlyList<SimulationCaseResult>> RunRequiredSuiteAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<SimulationCaseResult>(RequiredCases.Count);

        foreach (var testCase in RequiredCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _platform.InjectCustomerMessage(testCase.CustomerMessage, customerAlias: testCase.Name);

            var result = await _orchestrator.ProcessNextAsync(true, cancellationToken)
                ?? throw new InvalidOperationException($"测试用例 {testCase.Id} 未进入处理队列。");

            var passed = result.WasSent == testCase.ExpectAutoSend
                && result.Outcome.Contains(testCase.ExpectedOutcomeContains, StringComparison.Ordinal);

            results.Add(new SimulationCaseResult(
                testCase.Id,
                testCase.Name,
                passed,
                result.WasSent,
                result.Outcome,
                result.Decision.DraftReply));
        }

        return results;
    }
}
