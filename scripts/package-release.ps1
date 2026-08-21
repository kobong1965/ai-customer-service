param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\AgentDesk.App\AgentDesk.App.csproj'
$publishDirectory = Join-Path $projectRoot 'artifacts\AgentDesk-win-x64'
$stagingDirectory = Join-Path $projectRoot 'artifacts\AgentDesk-win-x64-staging'
$backupDirectory = Join-Path $projectRoot 'artifacts\AgentDesk-win-x64-backup'
$releaseDirectory = Join-Path $projectRoot 'release'
$packageName = 'AI-Customer-Service-win-x64.zip'
$checksumName = 'AI-Customer-Service-win-x64.sha256'
$packagePath = Join-Path $releaseDirectory $packageName
$checksumPath = Join-Path $releaseDirectory $checksumName
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "项目版本 $projectVersion 与打包版本 $Version 不一致。"
}

$expectedPublishRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
foreach ($directory in @($publishDirectory, $stagingDirectory, $backupDirectory)) {
    $resolvedDirectory = [System.IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith($expectedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "发布目录超出项目 artifacts 范围：$resolvedDirectory"
    }
}

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $backupDirectory) {
    Remove-Item -LiteralPath $backupDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

& $dotnetExecutable build (Join-Path $projectRoot 'AgentDesk.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnetExecutable test (Join-Path $projectRoot 'AgentDesk.sln') -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnetExecutable publish $projectFile -c Release -r win-x64 --self-contained true -o $stagingDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executablePath = Join-Path $stagingDirectory 'AgentDesk.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "自包含发布失败：未生成 $executablePath"
}

Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $packagePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName" -Encoding ascii

try {
    if (Test-Path -LiteralPath $publishDirectory) {
        Move-Item -LiteralPath $publishDirectory -Destination $backupDirectory
    }

    Move-Item -LiteralPath $stagingDirectory -Destination $publishDirectory
    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
catch {
    if (-not (Test-Path -LiteralPath $publishDirectory) -and (Test-Path -LiteralPath $backupDirectory)) {
        Move-Item -LiteralPath $backupDirectory -Destination $publishDirectory
    }

    throw
}

Write-Output "PACKAGE=$packagePath"
Write-Output "CHECKSUM=$checksumPath"
Write-Output "SHA256=$hash"
