using AgentDesk.Core;
using AgentDesk.Infrastructure;

namespace AgentDesk.IntegrationTests;

public sealed class MemoryAndSkillStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"AgentDesk-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RuntimeLearning_CreatesDeidentifiedCandidate_AndNeedsApprovalBeforeSearch()
    {
        var store = new FileExperienceMemoryStore(Path.Combine(_directory, "memories.json"));
        var incoming = new IncomingMessage(
            "message-1", "account-1", "王小明", "我手机13800138000，这条西裤 SKU-9 有货吗？",
            DateTimeOffset.Now, ProductKey: "SKU-9");
        var decision = new ReplyDecision(
            RiskLevel.Low, false, "有货", ["kb:stock:sku9"], [], ["skill:built-in:response-quality-review"]);

        await store.RecordAsync(new ProcessResult(incoming, decision, true, "已发送", null), CancellationToken.None);

        var candidate = Assert.Single(store.LoadAll());
        Assert.Equal(MemoryReviewStatus.Candidate, candidate.ReviewStatus);
        Assert.False(candidate.IsEnabled);
        Assert.DoesNotContain("王小明", candidate.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("13800138000", candidate.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("有货吗", candidate.Content, StringComparison.Ordinal);
        Assert.Contains("kb:stock:sku9", candidate.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.SearchApprovedAsync("库存", "account-1", "SKU-9", CancellationToken.None));

        store.Approve(candidate.Id);
        Assert.Single(await store.SearchApprovedAsync("库存", "account-1", "SKU-9", CancellationToken.None));
    }

    [Fact]
    public async Task Simulation_IsNeverLearned_AndDuplicateRuntimeEvidenceIsMerged()
    {
        var store = new FileExperienceMemoryStore(Path.Combine(_directory, "memories.json"));
        var decision = new ReplyDecision(RiskLevel.High, true, string.Empty, [], ["转人工"]);
        await store.RecordAsync(new ProcessResult(
            new IncomingMessage("s", "simulation-account", "test", "退款", DateTimeOffset.Now),
            decision, false, "转人工", null), CancellationToken.None);
        Assert.Empty(store.LoadAll());

        var live = new IncomingMessage("1", "account", "customer", "我要退款", DateTimeOffset.Now);
        await store.RecordAsync(new ProcessResult(live, decision, false, "转人工", null), CancellationToken.None);
        await store.RecordAsync(new ProcessResult(live with { Id = "2" }, decision, false, "转人工", null), CancellationToken.None);
        Assert.Equal(2, Assert.Single(store.LoadAll()).EvidenceCount);
    }

    [Fact]
    public async Task Skills_AreSeededMatchedToggleableAndRestorable()
    {
        var store = new FileAgentSkillStore(Path.Combine(_directory, "skills.json"));
        Assert.Equal(5, store.LoadAll().Count);

        var matches = await store.MatchAsync("我非常生气，要投诉", CancellationToken.None);
        Assert.InRange(matches.Count, 1, 4);
        Assert.Contains(matches, item => item.Id == "skill:built-in:angry-customer-deescalation");

        var angry = store.LoadAll().Single(item => item.Id == "skill:built-in:angry-customer-deescalation");
        store.ToggleEnabled(angry.Id);
        var disabledMatches = await store.MatchAsync("我非常生气，要投诉", CancellationToken.None);
        Assert.DoesNotContain(disabledMatches, item => item.Id == angry.Id);

        store.Delete(angry.Id);
        Assert.Equal(1, store.RestoreRecommended());
        Assert.Contains(store.LoadAll(), item => item.Id == angry.Id);
    }

    [Fact]
    public void ImportedSkill_IsDisabledUntilHumanApproval()
    {
        var sourcePath = Path.Combine(_directory, "source-skills.json");
        var targetPath = Path.Combine(_directory, "target-skills.json");
        var source = new FileAgentSkillStore(sourcePath);
        source.AddReviewed("订单追问", "只追问查询订单必需的信息", "流程", "订单",
            "请先确认当前界面是否存在可验证订单信息；不足时只追问必要条件。", "", "自定义", false);
        var target = new FileAgentSkillStore(targetPath);

        var added = target.ImportForReview(source.ExportJson());

        Assert.Equal(1, added);
        var imported = target.LoadAll().Single(item => item.Name == "订单追问");
        Assert.False(imported.IsReviewed);
        Assert.False(imported.IsEnabled);
        target.Approve(imported.Id);
        Assert.True(target.LoadAll().Single(item => item.Id == imported.Id).IsEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
