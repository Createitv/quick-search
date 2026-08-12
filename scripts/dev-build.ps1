[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'QuickSearch.sln'
$windowsProject = Join-Path $repoRoot 'src/QuickSearch.Windows/QuickSearch.Windows.csproj'
$testProject = Join-Path $repoRoot 'tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj'
$nativeDirectory = Join-Path $repoRoot 'src/QuickSearch.Windows/native'
$everythingDll = Join-Path $nativeDirectory 'Everything64.dll'

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'QuickSearch 的开发构建脚本需要在 Windows 上运行。'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '未找到 dotnet，请先安装 .NET 8 SDK。'
}

$hasDotNet8 = dotnet --list-sdks | Where-Object { $_ -match '^8\.' }
if (-not $hasDotNet8) {
    throw '未找到 .NET 8 SDK。'
}

if (-not (Test-Path $everythingDll)) {
    Write-Host '正在下载官方 Everything SDK...'
    $downloadDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "QuickSearch-dev-$([guid]::NewGuid().ToString('N'))"
    $archivePath = Join-Path $downloadDirectory 'Everything-SDK.zip'
    $extractPath = Join-Path $downloadDirectory 'Everything-SDK'

    New-Item -ItemType Directory -Path $downloadDirectory | Out-Null
    try {
        Invoke-WebRequest 'https://www.voidtools.com/Everything-SDK.zip' -OutFile $archivePath
        Expand-Archive -Path $archivePath -DestinationPath $extractPath
        $downloadedDll = Join-Path $extractPath 'dll/Everything64.dll'
        if (-not (Test-Path $downloadedDll)) {
            throw 'Everything SDK 中缺少 dll/Everything64.dll。'
        }

        New-Item -ItemType Directory -Force -Path $nativeDirectory | Out-Null
        Copy-Item -Path $downloadedDll -Destination $everythingDll
    }
    finally {
        if (Test-Path $downloadDirectory) {
            Remove-Item -Path $downloadDirectory -Recurse -Force
        }
    }
}

Push-Location $repoRoot
try {
    Write-Host '正在还原 NuGet 依赖...'
    dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore 失败，退出码：$LASTEXITCODE"
    }

    if (-not $SkipTests) {
        Write-Host '正在运行核心测试...'
        dotnet test $testProject -c Debug --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test 失败，退出码：$LASTEXITCODE"
        }
    }

    Write-Host '正在构建 QuickSearch Debug x64...'
    dotnet build $windowsProject -c Debug --no-restore -p:Platform=x64 --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失败，退出码：$LASTEXITCODE"
    }

    $executablePath = Join-Path $repoRoot 'src/QuickSearch.Windows/bin/Debug/net8.0-windows/QuickSearch.exe'
    Write-Host "开发构建完成：$executablePath"

    if ($Run) {
        Write-Host '正在启动 QuickSearch...'
        & $executablePath
    }
}
finally {
    Pop-Location
}
