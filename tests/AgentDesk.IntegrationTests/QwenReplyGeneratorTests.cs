using System.Net;
using System.Text;
using System.Text.Json;
using AgentDesk.AI;
using AgentDesk.Core;

namespace AgentDesk.IntegrationTests;

public sealed class QwenReplyGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_SendsVisionPayload_AndParsesGroundedDecision()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"亲，目前有库存。","factsUsed":["截图显示库存可售"],"warnings":[]}
            """));
        var generator = CreateGenerator(handler);
        var incoming = new IncomingMessage(
            "message-1",
            "account-1",
            "customer-1",
            "有货吗？",
            DateTimeOffset.Now,
            "data:image/png;base64,AA==");

        var decision = await generator.GenerateAsync(incoming, CancellationToken.None);

        Assert.False(decision.RequiresHuman);
        Assert.Equal(RiskLevel.Low, decision.RiskLevel);
        Assert.Contains("库存", decision.FactsUsed[0]);
        using var request = JsonDocument.Parse(handler.LastBody!);
        var body = request.RootElement;
        Assert.Equal("qwen3.7-plus", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("enable_thinking").GetBoolean());
        var content = body.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", content[0].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task GenerateAsync_EscalatesLowRiskOutput_WhenFactsAreMissing()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"可以的。","factsUsed":[],"warnings":[]}
            """));
        var generator = CreateGenerator(handler);

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "可以吗？", DateTimeOffset.Now),
            CancellationToken.None);

        Assert.True(decision.RequiresHuman);
        Assert.Equal(RiskLevel.High, decision.RiskLevel);
        Assert.Contains(decision.Warnings, warning => warning.Contains("依据", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ObserveAsync_RejectsCoordinatesOutsideWindow()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"action":"openConversation","clickX":1.2,"clickY":0.4,"confidence":0.9,"customerAlias":"A","latestCustomerMessage":"你好","summary":"未读"}
            """));
        var generator = CreateGenerator(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.ObserveAsync("data:image/png;base64,AA==", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_UsesExactSizingProfileAndRequiresItsFactId()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"亲，建议 L 码。","factsUsed":["size:test:trousers"],"warnings":[]}
            """));
        var profile = new ProductSizingProfile(
            "size:test:trousers",
            "https://shop.example.com/p/1",
            "SKU-1",
            "裤装",
            "西裤",
            "加长版",
            "全部账号",
            "请提供身高体重",
            [new SizeRecommendationRow("L", 170, 180, 60, 70, null, null, null, null, "")],
            true,
            true,
            DateTimeOffset.Now);
        var generator = CreateGenerator(handler, new StubSizingProvider([profile]));

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "加长版 175cm 65kg 穿什么码？", DateTimeOffset.Now, ProductKey: "SKU-1"),
            CancellationToken.None);

        Assert.False(decision.RequiresHuman);
        Assert.Contains("size:test:trousers", decision.FactsUsed);
        using var request = JsonDocument.Parse(handler.LastBody!);
        var prompt = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.Contains("加长版", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_EscalatesSizingReplyWithoutMatchingProfileFact()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"亲，建议 L 码。","factsUsed":["通用经验"],"warnings":[]}
            """));
        var generator = CreateGenerator(handler, new StubSizingProvider([]));

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "我穿什么尺码？", DateTimeOffset.Now, ProductKey: "SKU-X"),
            CancellationToken.None);

        Assert.True(decision.RequiresHuman);
        Assert.Equal(RiskLevel.High, decision.RiskLevel);
        Assert.Contains(decision.Warnings, warning => warning.Contains("精确匹配", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_EscalatesWhenModelSizeDisagreesWithRuleEvaluation()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"亲，建议 M 码。","factsUsed":["size:test:wrong"],"warnings":[]}
            """));
        var profile = new ProductSizingProfile(
            "size:test:wrong",
            "https://shop.example.com/p/2",
            "SKU-2",
            "上衣",
            "卫衣",
            "常规版",
            "全部账号",
            "请提供身高体重",
            [
                new SizeRecommendationRow("M", 160, 170, 50, 60, null, null, null, null, ""),
                new SizeRecommendationRow("L", 171, 180, 60.1, 70, null, null, null, null, "")
            ],
            true,
            true,
            DateTimeOffset.Now);
        var generator = CreateGenerator(handler, new StubSizingProvider([profile]));

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "175cm 65kg 穿什么码？", DateTimeOffset.Now, ProductKey: "SKU-2"),
            CancellationToken.None);

        Assert.True(decision.RequiresHuman);
        Assert.Equal(RiskLevel.High, decision.RiskLevel);
        Assert.Contains(decision.Warnings, warning => warning.Contains("不一致", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_IncludesApprovedMemoryAndMatchedSkillInPrompt()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"亲，当前页面显示有货。","factsUsed":["kb:stock:1"],"warnings":[],"skillsUsed":["skill:test:tone"]}
            """));
        var memory = new ExperienceMemory(
            "memory:test:stock", "库存咨询做法", "先引用当前界面库存依据，再简短回答。", "库存",
            "全部账号", "", MemoryReviewStatus.Approved, true, 0.9, 3, 0, "manual", DateTimeOffset.Now, DateTimeOffset.Now);
        var skill = new AgentSkill(
            "skill:test:tone", "简洁语气", "使用简短友好回复", "语气", [],
            "回复保持一到两句，不使用夸张承诺。", "", "test", true, true, true, DateTimeOffset.Now);
        var generator = CreateGenerator(handler, memoryProvider: new StubMemoryProvider([memory]), skillProvider: new StubSkillProvider([skill]));

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "有货吗？", DateTimeOffset.Now),
            CancellationToken.None);

        Assert.False(decision.RequiresHuman);
        Assert.Contains("skill:test:tone", decision.SkillsUsed!);
        using var request = JsonDocument.Parse(handler.LastBody!);
        var prompt = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("memory:test:stock", prompt, StringComparison.Ordinal);
        Assert.Contains("skill:test:tone", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_EscalatesUnknownSkillAndMemoryOnlyFacts()
    {
        var handler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"可以的。","factsUsed":["memory:test:1"],"warnings":[],"skillsUsed":["skill:unknown"]}
            """));
        var skill = new AgentSkill(
            "skill:test:allowed", "简洁语气", "使用简短友好回复", "语气", [],
            "回复保持一到两句，不使用夸张承诺。", "", "test", true, true, true, DateTimeOffset.Now);
        var generator = CreateGenerator(handler, skillProvider: new StubSkillProvider([skill]));

        var decision = await generator.GenerateAsync(
            new IncomingMessage("1", "account", "customer", "可以吗？", DateTimeOffset.Now),
            CancellationToken.None);

        Assert.True(decision.RequiresHuman);
        Assert.Contains(decision.Warnings, warning => warning.Contains("未提供", StringComparison.Ordinal));

        var memoryOnlyHandler = new RecordingHandler(JsonResponse("""
            {"riskLevel":"low","requiresHuman":false,"draftReply":"可以的。","factsUsed":["memory:test:1"],"warnings":[],"skillsUsed":["skill:test:allowed"]}
            """));
        var memoryOnlyGenerator = CreateGenerator(memoryOnlyHandler, skillProvider: new StubSkillProvider([skill]));
        var memoryOnlyDecision = await memoryOnlyGenerator.GenerateAsync(
            new IncomingMessage("2", "account", "customer", "可以吗？", DateTimeOffset.Now),
            CancellationToken.None);
        Assert.True(memoryOnlyDecision.RequiresHuman);
        Assert.Contains(memoryOnlyDecision.Warnings, warning => warning.Contains("事实依据", StringComparison.Ordinal));
    }

    private static QwenReplyGenerator CreateGenerator(
        HttpMessageHandler handler,
        IProductSizingProvider? sizingProvider = null,
        IExperienceMemoryProvider? memoryProvider = null,
        IAgentSkillProvider? skillProvider = null) => new(
        new HttpClient(handler),
        new QwenOptions(
            new Uri("https://example.test/compatible-mode/v1/chat/completions"),
            "test-key",
            "qwen3.7-plus",
            TimeSpan.FromSeconds(5)),
        productSizingProvider: sizingProvider,
        memoryProvider: memoryProvider,
        skillProvider: skillProvider);

    private static string JsonResponse(string content) => JsonSerializer.Serialize(new
    {
        choices = new[]
        {
            new { message = new { content } }
        }
    });

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubSizingProvider(IReadOnlyList<ProductSizingProfile> profiles) : IProductSizingProvider
    {
        public Task<IReadOnlyList<ProductSizingProfile>> FindAsync(
            string productKey,
            string query,
            string accountId,
            CancellationToken cancellationToken) => Task.FromResult(profiles);
    }

    private sealed class StubMemoryProvider(IReadOnlyList<ExperienceMemory> memories) : IExperienceMemoryProvider
    {
        public Task<IReadOnlyList<ExperienceMemory>> SearchApprovedAsync(string query, string accountId,
            string productKey, CancellationToken cancellationToken) => Task.FromResult(memories);
    }

    private sealed class StubSkillProvider(IReadOnlyList<AgentSkill> skills) : IAgentSkillProvider
    {
        public Task<IReadOnlyList<AgentSkill>> MatchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(skills);
    }
}
