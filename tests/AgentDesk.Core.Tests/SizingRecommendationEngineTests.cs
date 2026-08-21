using AgentDesk.Core;

namespace AgentDesk.Core.Tests;

public sealed class SizingRecommendationEngineTests
{
    [Fact]
    public void Evaluate_ReturnsUniqueMatchingSize()
    {
        var profile = Profile(
            Row("M", 50, 60),
            Row("L", 60.1, 70));

        var result = SizingRecommendationEngine.Evaluate(
            profile,
            new CustomerMeasurements(172, 65, null, null));

        Assert.Equal(SizingMatchStatus.Matched, result.Status);
        Assert.Equal("L", result.Row?.Size);
    }

    [Fact]
    public void Evaluate_RequiresEveryMeasurementUsedByProfile()
    {
        var profile = Profile(new SizeRecommendationRow(
            "M", 160, 175, 50, 65, 70, 82, null, null, string.Empty));

        var result = SizingRecommendationEngine.Evaluate(
            profile,
            new CustomerMeasurements(168, 58, null, null));

        Assert.Equal(SizingMatchStatus.MissingMeasurements, result.Status);
        Assert.Contains("腰围", result.Message);
    }

    [Fact]
    public void Evaluate_RejectsOverlappingRows()
    {
        var profile = Profile(
            Row("M", 50, 65),
            Row("L", 60, 75));

        var result = SizingRecommendationEngine.Evaluate(
            profile,
            new CustomerMeasurements(170, 62, null, null));

        Assert.Equal(SizingMatchStatus.MultipleMatches, result.Status);
        Assert.Null(result.Row);
    }

    private static ProductSizingProfile Profile(params SizeRecommendationRow[] rows) => new(
        "size:test",
        "https://example.com/product/1",
        "SKU-1",
        "裤装",
        "西裤",
        "常规版",
        "全部账号",
        "请提供身高体重",
        rows,
        true,
        true,
        DateTimeOffset.Now);

    private static SizeRecommendationRow Row(string size, double minimumWeight, double maximumWeight) => new(
        size, 150, 190, minimumWeight, maximumWeight, null, null, null, null, string.Empty);
}
