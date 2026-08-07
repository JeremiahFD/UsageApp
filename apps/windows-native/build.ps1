$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot "src"
$outputRoot = Join-Path $projectRoot "out"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The installed .NET Framework C# compiler was not found at $compiler."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter "*.cs" -File |
    ForEach-Object { $_.FullName }

if ($sources.Count -eq 0) {
    throw "No C# source files were found in $sourceRoot."
}

$outputPath = Join-Path $outputRoot "UsageApp.Native.exe"
$manifestPath = Join-Path $projectRoot "app.manifest"
$iconPath = Join-Path $outputRoot "UsageApp.Native.ico"
& (Join-Path $projectRoot "assets\New-UsageAppNativeIcon.ps1") -OutputPath $iconPath
$configPath = Join-Path $projectRoot "app.config"

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "The native Windows application configuration was not found at $configPath."
}

& $compiler `
    /nologo `
    /codepage:65001 `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /debug- `
    /win32manifest:"$manifestPath" `
    /win32icon:"$iconPath" `
    /out:"$outputPath" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Native Windows compilation failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $configPath -Destination "$outputPath.config" -Force

$item = Get-Item -LiteralPath $outputPath
[pscustomobject]@{
    Output = $item.FullName
    Bytes = $item.Length
    KiB = [math]::Round($item.Length / 1KB, 2)
}
