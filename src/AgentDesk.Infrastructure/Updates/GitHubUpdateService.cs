using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentDesk.Infrastructure.Updates;

public sealed record UpdateAsset(string Name, Uri DownloadUri, long Size);

public sealed record UpdateRelease(
    Version Version,
    string TagName,
    string Title,
    string Notes,
    Uri ReleasePage,
    UpdateAsset Package,
    UpdateAsset Checksum);

public sealed record PreparedUpdate(
    UpdateRelease Release,
    string PayloadDirectory,
    string ExecutablePath);

public sealed class GitHubUpdateService
{
    public const string DefaultPackageAssetName = "AI-Customer-Service-win-x64.zip";
    public const string DefaultChecksumAssetName = "AI-Customer-Service-win-x64.sha256";

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;
    private readonly string _updatesRoot;

    public GitHubUpdateService(
        HttpClient httpClient,
        string owner,
        string repository,
        string? updatesRoot = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _owner = RequireRepositoryPart(owner, nameof(owner));
        _repository = RequireRepositoryPart(repository, nameof(repository));
        _updatesRoot = updatesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "Updates");
    }

    public async Task<UpdateRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri($"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AI-Customer-Service", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub 更新服务返回 {(int)response.StatusCode}，请稍后重试。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = RequiredString(root, "tag_name");
        var version = ParseVersion(tagName);
        var title = OptionalString(root, "name") ?? tagName;
        var notes = OptionalString(root, "body") ?? "此版本没有填写更新说明。";
        var releasePage = RequiredHttpsUri(RequiredString(root, "html_url"), "Release 页面");

        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub Release 缺少下载资产列表。");
        }

        UpdateAsset? package = null;
        UpdateAsset? checksum = null;
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var asset = ParseAsset(assetElement);
            if (string.Equals(asset.Name, DefaultPackageAssetName, StringComparison.OrdinalIgnoreCase))
            {
                package = asset;
            }
            else if (string.Equals(asset.Name, DefaultChecksumAssetName, StringComparison.OrdinalIgnoreCase))
            {
                checksum = asset;
            }
        }

        return new UpdateRelease(
            version,
            tagName,
            title,
            notes,
            releasePage,
            package ?? throw new InvalidOperationException($"Release 缺少 {DefaultPackageAssetName}。"),
            checksum ?? throw new InvalidOperationException($"Release 缺少 {DefaultChecksumAssetName}。"));
    }

    public async Task<UpdateRelease?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        var release = await GetLatestReleaseAsync(cancellationToken);
        return release.Version > NormalizeVersion(currentVersion) ? release : null;
    }

    public async Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var versionDirectory = Path.Combine(_updatesRoot, release.Version.ToString(3));
        var payloadDirectory = Path.Combine(versionDirectory, "payload");
        var packagePath = Path.Combine(versionDirectory, DefaultPackageAssetName);
        var checksumPath = Path.Combine(versionDirectory, DefaultChecksumAssetName);

        Directory.CreateDirectory(_updatesRoot);
        if (Directory.Exists(versionDirectory))
        {
            Directory.Delete(versionDirectory, recursive: true);
        }

        Directory.CreateDirectory(versionDirectory);
        progress?.Report(2);
        await DownloadFileAsync(release.Package, packagePath, progress, 2, 82, cancellationToken);
        await DownloadFileAsync(release.Checksum, checksumPath, progress, 82, 88, cancellationToken);

        var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
        var expectedHash = ParseChecksum(checksumText);
        await VerifySha256Async(packagePath, expectedHash, cancellationToken);
        progress?.Report(91);

        ExtractPackageSafely(packagePath, payloadDirectory);
        var executablePath = Path.Combine(payloadDirectory, "AgentDesk.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException("更新包无效：根目录缺少 AgentDesk.exe。");
        }

        progress?.Report(100);
        return new PreparedUpdate(release, payloadDirectory, executablePath);
    }

    public static async Task VerifySha256Async(
        string filePath,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        var normalizedExpected = NormalizeSha256(expectedHash);
        await using var stream = File.OpenRead(filePath);
        var actualBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualHash = Convert.ToHexString(actualBytes);
        if (!string.Equals(actualHash, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新包 SHA-256 校验失败，已停止安装。请重新检查更新。");
        }
    }

    public static void ExtractPackageSafely(string packagePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包包含不安全的目录路径，已停止安装。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private async Task DownloadFileAsync(
        UpdateAsset asset,
        string destinationPath,
        IProgress<int>? progress,
        int progressStart,
        int progressEnd,
        CancellationToken cancellationToken)
    {
        EnsureHttps(asset.DownloadUri, $"资产 {asset.Name}");
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AI-Customer-Service", "1.0"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var expectedLength = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (expectedLength > 0)
            {
                var ratio = Math.Clamp((double)totalRead / expectedLength, 0, 1);
                progress?.Report(progressStart + (int)Math.Round((progressEnd - progressStart) * ratio));
            }
        }

        if (asset.Size > 0 && totalRead != asset.Size)
        {
            throw new InvalidOperationException($"资产 {asset.Name} 下载不完整，请重试。");
        }

        progress?.Report(progressEnd);
    }

    private static UpdateAsset ParseAsset(JsonElement element)
    {
        var name = RequiredString(element, "name");
        var uri = RequiredHttpsUri(RequiredString(element, "browser_download_url"), $"资产 {name}");
        var size = element.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
            ? parsedSize
            : 0;
        return new UpdateAsset(name, uri, size);
    }

    private static Version ParseVersion(string tagName)
    {
        var value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        if (!Version.TryParse(value, out var version))
        {
            throw new InvalidOperationException($"GitHub Release 版本号无效：{tagName}");
        }

        return NormalizeVersion(version);
    }

    private static Version NormalizeVersion(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));

    private static string ParseChecksum(string text)
    {
        var firstToken = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return NormalizeSha256(firstToken ?? string.Empty);
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("SHA-256 校验文件格式无效。");
        }

        return normalized.ToUpperInvariant();
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName)
        ?? throw new InvalidOperationException($"GitHub Release 缺少字段 {propertyName}。");

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;

    private static Uri RequiredHttpsUri(string value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{fieldName}地址无效。");
        }

        EnsureHttps(uri, fieldName);
        return uri;
    }

    private static void EnsureHttps(Uri uri, string fieldName)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{fieldName}必须使用 HTTPS。 ");
        }
    }

    private static string RequireRepositoryPart(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("GitHub 仓库名称无效。", parameterName);
        }

        return value;
    }
}
