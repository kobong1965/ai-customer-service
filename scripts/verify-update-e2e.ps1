$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $projectRoot 'release\AI-Customer-Service-win-x64.zip'
$testRoot = Join-Path $env:TEMP ("AgentDeskUpdateE2E-" + [guid]::NewGuid().ToString('N'))
$sourceDirectory = Join-Path $testRoot 'source'
$targetDirectory = Join-Path $testRoot 'target'
$targetExecutable = Join-Path $targetDirectory 'AgentDesk.exe'

try {
    New-Item -ItemType Directory -Path $sourceDirectory, $targetDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $packagePath -DestinationPath $sourceDirectory
    Set-Content -LiteralPath (Join-Path $targetDirectory 'preserve-user-marker.txt') -Value 'keep' -Encoding utf8

    $updater = Start-Process `
        -FilePath (Join-Path $sourceDirectory 'AgentDesk.exe') `
        -ArgumentList @(
            '--apply-update',
            '--parent-pid', '0',
            '--source', $sourceDirectory,
            '--target', $targetDirectory,
            '--executable', 'AgentDesk.exe') `
        -WorkingDirectory $sourceDirectory `
        -PassThru
    Wait-Process -Id $updater.Id -Timeout 45

    $deadline = (Get-Date).AddSeconds(30)
    $launched = $null
    do {
        $launched = Get-Process AgentDesk -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Path -and
                [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($targetExecutable)
            } |
            Select-Object -First 1
        if (-not $launched) { Start-Sleep -Milliseconds 250 }
    } while (-not $launched -and (Get-Date) -lt $deadline)

    if (-not $launched) {
        throw '更新后的程序没有自动重启。'
    }

    $windowDeadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $launched.Refresh()
    } while (-not $launched.HasExited -and
             [string]::IsNullOrWhiteSpace($launched.MainWindowTitle) -and
             (Get-Date) -lt $windowDeadline)
    if ($launched.HasExited -or [string]::IsNullOrWhiteSpace($launched.MainWindowTitle)) {
        throw '更新后的程序启动后异常退出，或没有创建主窗口。'
    }

    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($targetExecutable).ProductVersion
    $marker = Get-Content -LiteralPath (Join-Path $targetDirectory 'preserve-user-marker.txt') -Raw
    [pscustomobject]@{
        UpdateCliExit = $updater.ExitCode
        RestartedProcessId = $launched.Id
        InstalledVersion = $version
        WindowTitle = $launched.MainWindowTitle
        UnrelatedFilePreserved = ($marker.Trim() -eq 'keep')
    } | Format-List

    $null = $launched.CloseMainWindow()
    try {
        Wait-Process -Id $launched.Id -Timeout 10 -ErrorAction Stop
    }
    catch {
        Stop-Process -Id $launched.Id -Force
    }
}
finally {
    if ($launched -and -not $launched.HasExited) {
        Stop-Process -Id $launched.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $launched.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理非临时目录：$resolvedTestRoot"
    }

    if (Test-Path -LiteralPath $resolvedTestRoot) {
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            try {
                Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 19) {
                    Write-Warning "更新验证已完成，但临时目录稍后需清理：$resolvedTestRoot"
                    break
                }

                Start-Sleep -Milliseconds 500
            }
        }
    }
}
