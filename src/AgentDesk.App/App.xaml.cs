using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using AgentDesk.Infrastructure.Updates;

namespace AgentDesk.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (TryParseUpdateOptions(e.Args, out var updateOptions))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ApplyUpdateAndRestartAsync(updateOptions);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private async Task ApplyUpdateAndRestartAsync(UpdateInstallOptions options)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "Updates",
            "update.log");
        try
        {
            await UpdateInstaller.ApplyAsync(options);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(
                logPath,
                $"{DateTimeOffset.Now:O} 更新成功：{options.TargetDirectory}{Environment.NewLine}");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(options.TargetDirectory, options.ExecutableName),
                WorkingDirectory = options.TargetDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(
                logPath,
                $"{DateTimeOffset.Now:O} 更新失败：{exception}{Environment.NewLine}");
            MessageBox.Show(
                $"AI客服更新失败：{exception.Message}\n\n旧版本文件和本地数据仍保留。",
                "更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown();
        }
    }

    private static bool TryParseUpdateOptions(string[] args, out UpdateInstallOptions options)
    {
        options = null!;
        if (args.Length == 0 || !string.Equals(args[0], "--apply-update", StringComparison.Ordinal))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index + 1 < args.Length; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--parent-pid", out var parentText)
            || !int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId)
            || !values.TryGetValue("--source", out var sourceDirectory)
            || !values.TryGetValue("--target", out var targetDirectory)
            || !values.TryGetValue("--executable", out var executableName))
        {
            throw new InvalidOperationException("更新启动参数不完整。");
        }

        options = new UpdateInstallOptions(
            parentProcessId,
            sourceDirectory,
            targetDirectory,
            executableName);
        return true;
    }
}
