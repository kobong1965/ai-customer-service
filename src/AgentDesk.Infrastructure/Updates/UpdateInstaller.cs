using System.Diagnostics;

namespace AgentDesk.Infrastructure.Updates;

public sealed record UpdateInstallOptions(
    int ParentProcessId,
    string SourceDirectory,
    string TargetDirectory,
    string ExecutableName);

public static class UpdateInstaller
{
    public static async Task ApplyAsync(
        UpdateInstallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sourceDirectory = NormalizeDirectory(options.SourceDirectory, nameof(options.SourceDirectory));
        var targetDirectory = NormalizeDirectory(options.TargetDirectory, nameof(options.TargetDirectory));
        if (string.Equals(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新暂存目录不能与安装目录相同。");
        }

        if (string.IsNullOrWhiteSpace(options.ExecutableName)
            || Path.GetFileName(options.ExecutableName) != options.ExecutableName)
        {
            throw new InvalidOperationException("更新程序文件名无效。");
        }

        var sourceExecutable = Path.Combine(sourceDirectory, options.ExecutableName);
        if (!File.Exists(sourceExecutable))
        {
            throw new FileNotFoundException("更新暂存目录缺少主程序。", sourceExecutable);
        }

        await WaitForParentExitAsync(options.ParentProcessId, cancellationToken);
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var targetPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
            if (!targetPath.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新文件目标路径无效。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await CopyWithRetryAsync(sourcePath, targetPath, cancellationToken);
        }

        if (!File.Exists(Path.Combine(targetDirectory, options.ExecutableName)))
        {
            throw new InvalidOperationException("更新完成后没有找到主程序。");
        }
    }

    private static async Task WaitForParentExitAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        if (parentProcessId <= 0 || parentProcessId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(parentProcessId);
            for (var attempt = 0; attempt < 60 && !process.HasExited; attempt++)
            {
                await Task.Delay(500, cancellationToken);
                process.Refresh();
            }

            if (!process.HasExited)
            {
                throw new TimeoutException("旧版本在 30 秒内未退出，更新已取消。");
            }
        }
        catch (ArgumentException)
        {
            // The old process already exited between launch and lookup.
        }
    }

    private static async Task CopyWithRetryAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                return;
            }
            catch (IOException exception)
            {
                lastException = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastException = exception;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new IOException($"无法更新文件：{Path.GetFileName(targetPath)}", lastException);
    }

    private static string NormalizeDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("目录不能为空。", parameterName);
        }

        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar) == fullPath.TrimEnd(Path.DirectorySeparatorChar))
        {
            throw new InvalidOperationException("不允许把磁盘根目录用作更新目录。");
        }

        if (!Directory.Exists(fullPath) && parameterName == nameof(UpdateInstallOptions.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"更新暂存目录不存在：{fullPath}");
        }

        return fullPath;
    }
}
