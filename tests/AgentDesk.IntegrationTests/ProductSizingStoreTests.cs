using AgentDesk.Core;
using AgentDesk.Infrastructure;

namespace AgentDesk.IntegrationTests;

public sealed class ProductSizingStoreTests
{
    [Fact]
    public async Task Store_SeparatesVariantsAndMatchesAccountAndProduct()
    {
        var directory = TemporaryDirectory();
        var store = new FileProductSizingStore(Path.Combine(directory, "sizing.json"));
        try
        {
            store.AddReviewed(
                "https://shop.example.com/p/100#detail",
                "SKU-100",
                "裤装",
                "西裤",
                "常规版",
                "店铺A",
                "提供身高体重",
                [Row("M", 50, 60)]);
            store.AddReviewed(
                "https://shop.example.com/p/100",
                "SKU-100",
                "裤装",
                "西裤",
                "加长版",
                "店铺A",
                "提供身高体重",
                [Row("L", 55, 65)]);

            var matches = await store.FindAsync(
                "SKU-100",
                "这个加长版 60kg 穿多大？",
                "店铺A",
                CancellationToken.None);

            Assert.Single(matches);
            Assert.Equal("加长版", matches[0].Variant);
            Assert.Empty(await store.FindAsync("SKU-100", "尺码", "店铺B", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Store_RejectsDuplicateVariantAndChangesFingerprint()
    {
        var directory = TemporaryDirectory();
        var store = new FileProductSizingStore(Path.Combine(directory, "sizing.json"));
        try
        {
            var before = store.ComputeFingerprint();
            var item = store.AddReviewed(
                "https://shop.example.com/p/200",
                "SKU-200",
                "上衣",
                "卫衣",
                "常规版",
                "全部账号",
                "提供身高体重",
                [Row("L", 60, 70)]);
            var enabled = store.ComputeFingerprint();

            Assert.NotEqual(before, enabled);
            Assert.Throws<InvalidOperationException>(() => store.AddReviewed(
                "https://shop.example.com/p/200/",
                "SKU-200",
                "上衣",
                "卫衣",
                "常规版",
                "全部账号",
                "提供身高体重",
                [Row("XL", 70, 80)]));

            store.ToggleEnabled(item.Id);
            Assert.NotEqual(enabled, store.ComputeFingerprint());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Store_ExportsImportsReviewedRulesAndSkipsDuplicates()
    {
        var directory = TemporaryDirectory();
        var source = new FileProductSizingStore(Path.Combine(directory, "source.json"));
        var target = new FileProductSizingStore(Path.Combine(directory, "target.json"));
        try
        {
            source.AddReviewed(
                "https://shop.example.com/p/300",
                "SKU-300",
                "裤装",
                "阔腿裤",
                "常规版",
                "全部账号",
                "提供身高体重",
                [Row("M", 45, 58)]);

            var json = source.ExportJson();
            Assert.Equal(1, target.ImportReviewed(json));
            Assert.Equal(0, target.ImportReviewed(json));
            Assert.Single(target.LoadAll());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static SizeRecommendationRow Row(string size, double minWeight, double maxWeight) => new(
        size, 150, 190, minWeight, maxWeight, null, null, null, null, string.Empty);

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
