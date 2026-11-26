[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '请按 docs/windows.md 在项目 .tools/dotnet 解包指定版本的 SDK。' }
Push-Location $root
try {
    & $dotnet restore src/Yxrl/Yxrl.csproj --locked-mode --packages (Join-Path $root '.tools/packages')
    if ($LASTEXITCODE -ne 0) { throw '还原依赖失败' }
    & $dotnet publish src/Yxrl/Yxrl.csproj -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o dist/win-x64
    if ($LASTEXITCODE -ne 0) { throw '发布失败' }
    Get-Item -LiteralPath (Join-Path $root 'dist/win-x64/Yxrl.exe') | Select-Object FullName, Length
} finally { Pop-Location }
