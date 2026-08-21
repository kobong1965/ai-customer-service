using System.Text.RegularExpressions;

namespace AgentDesk.IntegrationTests;

public sealed class UiScaleContractTests
{
    private static readonly string WorkspaceRoot = FindWorkspaceRoot();

    [Fact]
    public void DesignTokens_KeepReadableDesktopScale()
    {
        var tokens = ReadAppFile("Themes", "DesignTokens.xaml");

        Assert.Contains("x:Key=\"FontSizeBody\">16<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FontSizeSecondary\">14<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FontSizeControl\">16<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FontSizeSectionTitle\">20<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FontSizePageTitle\">28<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ControlHeight\">44<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableRowHeight\">52<", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ContentMaxWidth\">1920<", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DoesNotReintroduceLiteralFontSizes()
    {
        var xaml = ReadAppFile("MainWindow.xaml");

        Assert.DoesNotMatch(new Regex("FontSize=\"[0-9]", RegexOptions.CultureInvariant), xaml);
        Assert.Contains("Grid.Row=\"1\" HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\" AutomationProperties.Name=\"商品尺码规则表\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"{StaticResource ContentMaxWidth}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsManifest_UsesPerMonitorV2DpiAwareness()
    {
        var manifest = ReadAppFile("app.manifest");

        Assert.Contains("PerMonitorV2,PerMonitor", manifest, StringComparison.Ordinal);
        Assert.Contains("true/pm", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Branding_UsesAiCustomerServiceNameAndMultiSizeIcon()
    {
        var xaml = ReadAppFile("MainWindow.xaml");
        var project = ReadAppFile("AgentDesk.App.csproj");
        var icon = File.ReadAllBytes(Path.Combine(
            WorkspaceRoot, "src", "AgentDesk.App", "Assets", "ai-customer-service.ico"));

        Assert.Contains("Title=\"AI客服\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"AI客服\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ai-customer-service-logo.png", xaml, StringComparison.Ordinal);
        Assert.Contains("<Product>AI客服</Product>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\ai-customer-service.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Equal(7, BitConverter.ToUInt16(icon, 4));
    }

    [Fact]
    public void SoftwareUpdate_UsesPublicReleaseIntegrityAndAccessibleControls()
    {
        var xaml = ReadAppFile("MainWindow.xaml");
        var project = ReadAppFile("AgentDesk.App.csproj");
        var updateService = ReadWorkspaceFile(
            "src", "AgentDesk.Infrastructure", "Updates", "GitHubUpdateService.cs");
        var workflow = ReadWorkspaceFile(".github", "workflows", "release.yml");

        Assert.Contains("<Version>0.5.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"检查软件更新\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"下载并安装软件更新\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding UpdateProgress, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ApplicationVersionText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AI客服 0.4.2", xaml, StringComparison.Ordinal);
        Assert.Contains("AI-Customer-Service-win-x64.sha256", updateService, StringComparison.Ordinal);
        Assert.Contains("ExtractPackageSafely", updateService, StringComparison.Ordinal);
        Assert.Contains("permissions:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
    }

    private static string ReadAppFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine([WorkspaceRoot, "src", "AgentDesk.App", .. pathParts]));
    }

    private static string ReadWorkspaceFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine([WorkspaceRoot, .. pathParts]));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentDesk.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AgentDesk workspace root.");
    }
}
