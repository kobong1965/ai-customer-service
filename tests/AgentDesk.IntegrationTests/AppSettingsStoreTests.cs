using AgentDesk.Infrastructure;

namespace AgentDesk.IntegrationTests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SettingsFile_DoesNotContainApiKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = new FileAppSettingsStore(path);
        var settings = AppSettings.Default with
        {
            ModelVerifiedFingerprint = "safe-fingerprint"
        };

        try
        {
            await store.SaveAsync(settings);
            var json = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(settings, await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Theory]
    [InlineData(0.44, 0.96, 0.59, 0.96, true)]
    [InlineData(-0.1, 0.96, 0.59, 0.96, false)]
    [InlineData(0.44, 0.96, 1.1, 0.96, false)]
    public void Calibration_ValidatesRelativeCoordinates(
        double inputX,
        double inputY,
        double sendX,
        double sendY,
        bool expected)
    {
        var calibration = new PlatformCalibrationSettings(
            "客服平台",
            inputX,
            inputY,
            sendX,
            sendY,
            1500,
            0.85);

        Assert.Equal(expected, calibration.IsValid);
    }

    [Fact]
    public void WindowsCredentialStore_RoundTripsAndDeletesSecret()
    {
        var store = new WindowsCredentialSecretStore($"AgentDesk-Test-{Guid.NewGuid():N}");
        const string key = "temporary-key";
        const string secret = "sk-test-only-not-a-real-key";

        try
        {
            store.Write(key, secret);
            Assert.Equal(secret, store.Read(key));
            store.Delete(key);
            Assert.Null(store.Read(key));
        }
        finally
        {
            store.Delete(key);
        }
    }

    [Fact]
    public void RunEventStore_PersistsRecentEventsWithoutMultilineContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "events.jsonl");
        var store = new FileRunEventStore(path);

        try
        {
            store.Append(new AgentDesk.Core.RunEvent(
                DateTimeOffset.Now,
                "account",
                AgentDesk.Core.AgentStage.Monitoring,
                "开始\r\n观察"));
            var events = store.ReadRecent();

            Assert.Single(events);
            Assert.Equal("开始  观察", events[0].Summary);
            Assert.DoesNotContain('\n', File.ReadAllText(path).Trim());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task KnowledgeStore_SearchesOnlyReviewedEnabledMatchingItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "knowledge.json");
        var store = new FileKnowledgeStore(path);

        try
        {
            var stock = store.AddReviewed("黑色西裤库存", "黑色 3XL 当前有库存。", "全部账号");
            store.AddReviewed("发货时效", "现货订单 48 小时内发出。", "全部账号");

            var results = await store.SearchAsync("黑色 3XL 还有货吗？", "account", CancellationToken.None);
            Assert.Single(results);
            Assert.Equal(stock.Id, results[0].Id);

            store.ToggleEnabled(stock.Id);
            results = await store.SearchAsync("黑色 3XL 还有货吗？", "account", CancellationToken.None);
            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void KnowledgeStore_UpdatesDeletesAndRejectsDuplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "knowledge.json");
        var store = new FileKnowledgeStore(path);

        try
        {
            var item = store.AddReviewed("发货时效", "现货 48 小时内发出。", "全部账号");
            Assert.Throws<InvalidOperationException>(() =>
                store.AddReviewed("发货时效", "现货 48 小时内发出。", "全部账号"));

            var updated = store.UpdateReviewed(
                item.Id,
                "发货时效说明",
                "现货订单通常在 48 小时内发出。",
                "店铺A");
            Assert.Equal("店铺A", updated.AccountScope);
            Assert.Single(store.LoadAll());

            store.Delete(item.Id);
            Assert.Empty(store.LoadAll());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void KnowledgeStore_ImportsReviewedItemsAndSkipsDuplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(directory, "source.json");
        var targetPath = Path.Combine(directory, "target.json");
        var source = new FileKnowledgeStore(sourcePath);
        var target = new FileKnowledgeStore(targetPath);

        try
        {
            source.AddReviewed("尺码建议", "尺码建议只作为参考。", "全部账号");
            var json = source.ExportJson();

            Assert.Equal(1, target.ImportReviewed(json));
            Assert.Equal(0, target.ImportReviewed(json));
            Assert.Single(target.LoadAll());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void Calibration_DetectsWindowSizeDrift()
    {
        var calibration = PlatformCalibrationSettings.Default with
        {
            WindowTitleContains = "客服平台",
            CapturedWidth = 1400,
            CapturedHeight = 900
        };

        Assert.True(calibration.IsWindowSizeStable(1500, 850));
        Assert.False(calibration.IsWindowSizeStable(1700, 900));
        Assert.False(calibration.IsWindowSizeStable(1400, 700));
    }

    [Fact]
    public void RunEventStore_ExportsAndClearsEvents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "events.jsonl");
        var store = new FileRunEventStore(path);

        try
        {
            store.Append(new AgentDesk.Core.RunEvent(
                DateTimeOffset.Now,
                "account",
                AgentDesk.Core.AgentStage.ShadowObserved,
                "影子观察完成"));
            Assert.NotEmpty(store.ExportJsonLines());
            Assert.Equal("影子观察完成", Assert.Single(store.ReadRecent()).Summary);

            store.Clear();
            Assert.Empty(store.ReadRecent());
            Assert.Equal(string.Empty, store.ExportJsonLines());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
