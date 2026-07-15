<#
  发布打包：软件一包、数据一包。
  软件包 = 自包含发布（目标机不用装 .NET）。
  数据包 = 只含运行时需要的白名单，绝不带 pipeline 源码/中间产物。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\publish\pack.ps1
    参数 -SkipData 只打软件包（数据没变时，1.7G 不用重压）

  产物落在 dist\ ：
    HorizonGuide-app-win-x64.zip
    HorizonGuide-data.zip
#>
param(
    [string]$Rid = "win-x64",
    [switch]$SkipData
)
$ErrorActionPreference = 'Stop'

$Root    = (Resolve-Path "$PSScriptRoot\..\..").Path
$OutDir  = Join-Path $Root "dist"
$AppProj = Join-Path $Root "src\HorizonGuide.App\HorizonGuide.App.csproj"
$StageApp  = Join-Path $OutDir "stage-app"
$StageData = Join-Path $OutDir "stage-data"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Zip($srcDir, $zipPath, $level) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $srcDir, $zipPath,
        [System.IO.Compression.CompressionLevel]::$level, $false)
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

# ---------- 1. 软件包 ----------
Write-Host "==> 发布软件（自包含 $Rid）..." -ForegroundColor Cyan
if (Test-Path $StageApp) { Remove-Item -Recurse -Force $StageApp }
dotnet publish $AppProj -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $StageApp
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# 发布 README 进软件包根目录
Copy-Item (Join-Path $Root "docs\RELEASE_README.md") (Join-Path $StageApp "README.md") -Force

$AppZip = Join-Path $OutDir "HorizonGuide-app-$Rid.zip"
Write-Host "==> 压缩软件包 -> $AppZip"
Zip $StageApp $AppZip Optimal

# ---------- 2. 数据包 ----------
if (-not $SkipData) {
    Write-Host "==> 收集数据白名单..." -ForegroundColor Cyan
    if (Test-Path $StageData) { Remove-Item -Recurse -Force $StageData }
    New-Item -ItemType Directory -Force (Join-Path $StageData "data") | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $StageData "content") | Out-Null

    Copy-Item (Join-Path $Root "data\survey-drafts.json")       (Join-Path $StageData "data\survey-drafts.json") -Force
    Copy-Item (Join-Path $Root "content\content-index.json")    (Join-Path $StageData "content\content-index.json") -Force
    Copy-Item -Recurse (Join-Path $Root "content\audio")        (Join-Path $StageData "content\audio") -Force
    # 注意：故意不带 content\facts、content\scripts、content\_old_*、data\*.log、
    #       *-logs\、以及整个 tools\ —— 那些是 pipeline 的内幕，不发。

    $DataZip = Join-Path $OutDir "HorizonGuide-data.zip"
    Write-Host "==> 压缩数据包（wav 基本压不动，用 Fastest）-> $DataZip"
    Zip $StageData $DataZip Fastest
}

# ---------- 收尾 ----------
Remove-Item -Recurse -Force $StageApp -ErrorAction SilentlyContinue
if (-not $SkipData) { Remove-Item -Recurse -Force $StageData -ErrorAction SilentlyContinue }

Write-Host "`n完成。产物：" -ForegroundColor Green
Get-ChildItem $OutDir -Filter *.zip | ForEach-Object {
    "{0,-36} {1,8:N0} MB" -f $_.Name, ($_.Length/1MB)
}
