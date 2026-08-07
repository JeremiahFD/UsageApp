[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
trap {
    $message = "UsageApp Native could not be installed: " + $_.Exception.Message
    if (-not $Quiet) {
        try {
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show(
                $message,
                "UsageApp Native setup",
                [System.Windows.MessageBoxButton]::OK,
                [System.Windows.MessageBoxImage]::Error) | Out-Null
        }
        catch { }
    }
    [Console]::Error.WriteLine($message)
    exit 1
}

$sourceExe = Join-Path $SourceDirectory "UsageApp.Native.exe"
$sourceConfig = Join-Path $SourceDirectory "UsageApp.Native.exe.config"
$sourceUninstaller = Join-Path $SourceDirectory "Uninstall-UsageAppNative.ps1"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "UsageApp.Native.exe was not found beside this installer."
}
if (-not (Test-Path -LiteralPath $sourceConfig -PathType Leaf)) {
    throw "UsageApp.Native.exe.config was not found beside this installer."
}
if (-not (Test-Path -LiteralPath $sourceUninstaller -PathType Leaf)) {
    throw "Uninstall-UsageAppNative.ps1 was not found beside this installer."
}

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\UsageApp Native"
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null

$destinationExe = Join-Path $installDirectory "UsageApp.Native.exe"
$destinationConfig = Join-Path $installDirectory "UsageApp.Native.exe.config"
$destinationUninstaller = Join-Path $installDirectory "Uninstall-UsageAppNative.ps1"

# A running executable cannot be replaced on Windows. Stop only the installed
# UsageApp Native process at this exact per-user path, leaving the Electron app
# and development copies untouched.
Get-Process -Name "UsageApp.Native" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [string]::Equals($_.Path, $destinationExe,
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    } |
    ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction Stop
        $_.WaitForExit(10000) | Out-Null
    }

if (Get-Process -Name "UsageApp.Native" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [string]::Equals($_.Path, $destinationExe,
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    }) {
    throw "UsageApp Native is still running. Exit it from the tray menu and run setup again."
}

$temporaryExe = $destinationExe + ".new"
Copy-Item -LiteralPath $sourceExe -Destination $temporaryExe -Force
Move-Item -LiteralPath $temporaryExe -Destination $destinationExe -Force
Copy-Item -LiteralPath $sourceConfig -Destination $destinationConfig -Force
Copy-Item -LiteralPath $sourceUninstaller -Destination $destinationUninstaller -Force

$shortcutDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
$shortcutPath = Join-Path $shortcutDirectory "UsageApp Native.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destinationExe
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = "UsageApp Native Windows beta"
$shortcut.Save()

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\UsageAppNative"
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallCommand = 'powershell.exe -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $destinationUninstaller + '"'
$quietUninstallCommand = $uninstallCommand + ' -Quiet'
$estimatedSize = [int][Math]::Ceiling((
    (Get-Item -LiteralPath $destinationExe).Length +
    (Get-Item -LiteralPath $destinationConfig).Length +
    (Get-Item -LiteralPath $destinationUninstaller).Length) / 1KB)
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value "UsageApp Native (Beta)" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value "0.2.0-beta.1" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value "JeremiahFD" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDirectory -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $destinationExe -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value $quietUninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name URLInfoAbout -Value "https://github.com/JeremiahFD/UsageApp" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $estimatedSize -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

if (-not $Quiet) {
    Start-Process -FilePath $destinationExe -ArgumentList "--show"
}
