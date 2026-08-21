using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentDesk.Infrastructure.Updates;

namespace AgentDesk.IntegrationTests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNewerReleaseWithRequiredAssets()
    {
        var handler = new StubHttpMessageHandler(request =>
            JsonResponse(BuildReleaseJson(new Version(0, 6, 0), packageSize: 10, checksumSize: 72)));
        var service = new GitHubUpdateService(
            new HttpClient(handler),
            "owner",
            "repository",
            CreateTemporaryDirectory());

        var release = await service.CheckForUpdateAsync(new Version(0, 5, 0, 0));

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 6, 0), release.Version);
        Assert.Equal(GitHubUpdateService.DefaultPackageAssetName, release.Package.Name);
        Assert.Equal(GitHubUpdateService.DefaultChecksumAssetName, release.Checksum.Name);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullWhenCurrentVersionIsLatest()
    {
        var handler = new StubHttpMessageHandler(request =>
            JsonResponse(BuildReleaseJson(new Version(0, 5, 0), packageSize: 10, checksumSize: 72)));
        var service = new GitHubUpdateService(
            new HttpClient(handler),
            "owner",
            "repository",
            CreateTemporaryDirectory());

        var release = await service.CheckForUpdateAsync(new Version(0, 5, 0, 9));

        Assert.Null(release);
    }

    [Fact]
    public async Task PrepareUpdateAsync_DownloadsVerifiesAndExtractsPackage()
    {
        var packageBytes = CreatePackage(("AgentDesk.exe", "new executable"), ("Assets/logo.txt", "logo"));
        var checksum = Convert.ToHexString(SHA256.HashData(packageBytes));
        var checksumBytes = Encoding.UTF8.GetBytes($"{checksum}  {GitHubUpdateService.DefaultPackageAssetName}\n");
        var releaseJson = BuildReleaseJson(
            new Version(0, 6, 0),
            packageBytes.LongLength,
            checksumBytes.LongLength);
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/release/package" => BinaryResponse(packageBytes, "application/zip"),
            "/release/checksum" => BinaryResponse(checksumBytes, "text/plain"),
            _ => JsonResponse(releaseJson)
        });
        var updateRoot = CreateTemporaryDirectory();
        var service = new GitHubUpdateService(new HttpClient(handler), "owner", "repository", updateRoot);
        var release = await service.GetLatestReleaseAsync();

        var prepared = await service.PrepareUpdateAsync(release);

        Assert.True(File.Exists(prepared.ExecutablePath));
        Assert.Equal("new executable", await File.ReadAllTextAsync(prepared.ExecutablePath));
        Assert.True(File.Exists(Path.Combine(prepared.PayloadDirectory, "Assets", "logo.txt")));
    }

    [Fact]
    public async Task VerifySha256Async_RejectsMismatchedPackage()
    {
        var directory = CreateTemporaryDirectory();
        var packagePath = Path.Combine(directory, "package.zip");
        await File.WriteAllTextAsync(packagePath, "tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GitHubUpdateService.VerifySha256Async(packagePath, new string('0', 64)));

        Assert.Contains("SHA-256 校验失败", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractPackageSafely_RejectsPathTraversal()
    {
        var directory = CreateTemporaryDirectory();
        var packagePath = Path.Combine(directory, "unsafe.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("unsafe");
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GitHubUpdateService.ExtractPackageSafely(packagePath, Path.Combine(directory, "payload")));

        Assert.Contains("不安全", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, "outside.txt")));
    }

    [Fact]
    public async Task UpdateInstaller_OverwritesProgramFilesAndPreservesUnrelatedFiles()
    {
        var root = CreateTemporaryDirectory();
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(source, "AgentDesk.exe"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "AgentDesk.dll"), "new dll");
        await File.WriteAllTextAsync(Path.Combine(target, "AgentDesk.exe"), "old");
        await File.WriteAllTextAsync(Path.Combine(target, "local-user-file.json"), "keep");

        await UpdateInstaller.ApplyAsync(new UpdateInstallOptions(0, source, target, "AgentDesk.exe"));

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, "AgentDesk.exe")));
        Assert.Equal("new dll", await File.ReadAllTextAsync(Path.Combine(target, "AgentDesk.dll")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(target, "local-user-file.json")));
    }

    private static string BuildReleaseJson(Version version, long packageSize, long checksumSize) =>
        JsonSerializer.Serialize(new
        {
            tag_name = $"v{version.ToString(3)}",
            name = $"AI客服 {version.ToString(3)}",
            body = "测试更新说明",
            html_url = "https://github.com/owner/repository/releases/latest",
            assets = new object[]
            {
                new
                {
                    name = GitHubUpdateService.DefaultPackageAssetName,
                    browser_download_url = "https://downloads.example/release/package",
                    size = packageSize
                },
                new
                {
                    name = GitHubUpdateService.DefaultChecksumAssetName,
                    browser_download_url = "https://downloads.example/release/checksum",
                    size = checksumSize
                }
            }
        });

    private static byte[] CreatePackage(params (string Path, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return output.ToArray();
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BinaryResponse(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = bytes.LongLength;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AgentDeskTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
