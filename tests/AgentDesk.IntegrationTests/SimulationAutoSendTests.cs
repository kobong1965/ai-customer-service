using AgentDesk.AI;
using AgentDesk.Automation;

namespace AgentDesk.IntegrationTests;

public sealed class SimulationAutoSendTests
{
    [Fact]
    public async Task MandatorySuite_PassesAllFiveSafetyCases()
    {
        var service = new ManualSimulationService(new RuleBasedReplyGenerator());

        var results = await service.RunRequiredSuiteAsync();

        Assert.Equal(5, results.Count);
        Assert.All(results, result => Assert.True(result.Passed, result.Outcome));
        Assert.Equal(2, results.Count(result => result.WasSent));
        Assert.Equal(3, results.Count(result => !result.WasSent));
    }

    [Theory]
    [InlineData("黑色 3XL 还有货吗？", true)]
    [InlineData("今天下单什么时候发货？", true)]
    [InlineData("我要投诉并要求赔偿", false)]
    [InlineData("帮我修改地址", false)]
    [InlineData("保证永远不会坏吗？", false)]
    public async Task ManualSimulation_OnlyAutoSendsLowRiskMessages(
        string message,
        bool expectedSent)
    {
        var service = new ManualSimulationService(new RuleBasedReplyGenerator());

        var result = await service.SendManualMessageAsync(message);

        Assert.Equal(expectedSent, result.WasSent);
    }
}
