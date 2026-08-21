$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetExecutable = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetExecutable)) {
    throw "未找到项目内 .NET SDK：$dotnetExecutable"
}

& $dotnetExecutable build (Join-Path $projectRoot 'AgentDesk.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnetExecutable test (Join-Path $projectRoot 'AgentDesk.sln') -c Release --no-build
exit $LASTEXITCODE
