[CmdletBinding()]
param(
    [string]$Configuration = "Beta",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outputExe = Join-Path $projectRoot "out\UsageApp.Native.exe"
$outputConfig = Join-Path $projectRoot "out\UsageApp.Native.exe.config"
& (Join-Path $projectRoot "build.ps1")

$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
if (-not (Test-Path -LiteralPath $iexpress -PathType Leaf)) {
    throw "Windows IExpress was not found."
}

$releaseDirectory = Join-Path $projectRoot "release"
$stageDirectory = Join-Path $releaseDirectory "installer-stage"
New-Item -ItemType Directory -Force -Path $stageDirectory | Out-Null
Copy-Item -LiteralPath $outputExe -Destination (Join-Path $stageDirectory "UsageApp.Native.exe") -Force
Copy-Item -LiteralPath $outputConfig -Destination (Join-Path $stageDirectory "UsageApp.Native.exe.config") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Install-UsageAppNative.ps1") -Destination (Join-Path $stageDirectory "Install-UsageAppNative.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Uninstall-UsageAppNative.ps1") -Destination (Join-Path $stageDirectory "Uninstall-UsageAppNative.ps1") -Force

$installerName = "UsageApp-Native-$Configuration-Setup.exe"
$installerPath = Join-Path $releaseDirectory $installerName
$sedPath = Join-Path $stageDirectory "usageapp-native.sed"
$escapedStage = $stageDirectory.Replace("\\", "\\")
$escapedInstaller = $installerPath.Replace("\\", "\\")

@"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$escapedInstaller
FriendlyName=UsageApp Native $Configuration
AppLaunched=powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File Install-UsageAppNative.ps1 -SourceDirectory . $(if ($NoLaunch) { '-Quiet' })
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0="UsageApp.Native.exe"
FILE1="Install-UsageAppNative.ps1"
FILE2="UsageApp.Native.exe.config"
FILE3="Uninstall-UsageAppNative.ps1"
[SourceFiles]
SourceFiles0=$escapedStage
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
"@ | Set-Content -LiteralPath $sedPath -Encoding ASCII

$iexpressProcess = Start-Process -FilePath $iexpress -ArgumentList @('/N', $sedPath) -Wait -PassThru
if ($iexpressProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
    throw "IExpress did not create the installer."
}

Get-Item -LiteralPath $installerPath | Select-Object FullName, Length

$portablePath = Join-Path $releaseDirectory "UsageApp-Native-$Configuration-Portable-x64.zip"
Remove-Item -LiteralPath $portablePath -Force -ErrorAction SilentlyContinue
Compress-Archive -LiteralPath @(
    (Join-Path $stageDirectory "UsageApp.Native.exe"),
    (Join-Path $stageDirectory "UsageApp.Native.exe.config")
) -DestinationPath $portablePath -CompressionLevel Optimal
Get-Item -LiteralPath $portablePath | Select-Object FullName, Length
