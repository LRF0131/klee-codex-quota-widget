param(
    [switch]$Install,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectDir 'src\Program.cs'
$dist = Join-Path $projectDir 'dist'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $dist 'KleeCodexQuotaWidget.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 Windows C# 编译器：$compiler"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$package = Get-AppxPackage OpenAI.Codex | Select-Object -First 1
$logo = Join-Path $package.InstallLocation 'assets\Square44x44Logo.targetsize-32_altform-unplated.png'
if (-not (Test-Path -LiteralPath $logo)) { throw '找不到 Codex Logo 资源' }
$appIcon = Join-Path $package.InstallLocation 'app\resources\icon-chatgpt.ico'
if (-not (Test-Path -LiteralPath $appIcon)) { throw '找不到 Codex 应用图标资源' }
$manifest = Join-Path $projectDir 'src\app.manifest'
if (-not (Test-Path -LiteralPath $manifest)) { throw '找不到应用清单' }
if (Test-Path -LiteralPath $exe) {
    Get-Process -Name 'KleeCodexQuotaWidget' -ErrorAction SilentlyContinue | Stop-Process -Force
}
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /out:$exe `
    "/win32icon:$appIcon" `
    "/win32manifest:$manifest" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    "/resource:$logo,CodexKnot.png" `
    $source
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码：$LASTEXITCODE" }

if ($Install) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-ItemProperty -Path $runKey -Name 'KleeCodexQuotaWidget' -PropertyType String -Value ('"' + $exe + '"') -Force | Out-Null
}

if ($Run) {
    Start-Process -FilePath $exe -WindowStyle Hidden
}

Write-Output "Built: $exe"
if ($Install) { Write-Output 'Startup: enabled for current user' }
