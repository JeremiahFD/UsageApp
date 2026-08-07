[CmdletBinding()]
param(
    [switch]$Quiet,
    [switch]$InternalStage
)

$ErrorActionPreference = "Stop"
trap {
    $message = "UsageApp Native could not be completely removed: " +
        $_.Exception.Message +
        "`n`nThe installer stopped to avoid leaving provider settings in an unsafe state."
    if (-not $Quiet) {
        try {
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show(
                $message,
                "UsageApp Native uninstall",
                [System.Windows.MessageBoxButton]::OK,
                [System.Windows.MessageBoxImage]::Error) | Out-Null
        }
        catch { }
    }
    [Console]::Error.WriteLine($message)
    exit 1
}

if (-not $InternalStage) {
    $stagedScript = Join-Path $env:TEMP (
        "UsageAppNative-Uninstall-" + [Guid]::NewGuid().ToString("N") + ".ps1")
    Copy-Item -LiteralPath $PSCommandPath -Destination $stagedScript -Force
    $quotedStage = '"' + $stagedScript.Replace('"', '\"') + '"'
    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File ' +
        $quotedStage + ' -InternalStage' + $(if ($Quiet) { ' -Quiet' } else { '' })
    $child = Start-Process `
        -FilePath powershell.exe `
        -ArgumentList $arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    Remove-Item -LiteralPath $stagedScript -Force -ErrorAction SilentlyContinue
    exit $child.ExitCode
}

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\UsageApp Native"
$destinationExe = Join-Path $installDirectory "UsageApp.Native.exe"
$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\UsageApp Native.lnk"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\UsageAppNative"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

if (Test-Path -LiteralPath $destinationExe -PathType Leaf) {
    $reportPath = Join-Path $env:TEMP ("usageapp-claude-uninstall-" + [Guid]::NewGuid().ToString("N") + ".txt")
    try {
        $quotedReportPath = '"' + $reportPath.Replace('"', '\"') + '"'
        $disconnect = Start-Process `
            -FilePath $destinationExe `
            -ArgumentList @('--disconnect-claude-output', $quotedReportPath) `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        $disconnectExit = $disconnect.ExitCode
        $reportExists = Test-Path -LiteralPath $reportPath -PathType Leaf
        if ($disconnectExit -ne 0 -or -not $reportExists) {
            $detail = if (Test-Path -LiteralPath $reportPath) {
                (Get-Content -LiteralPath $reportPath -Raw).Trim()
            } else {
                "Claude's status-line setting could not be safely restored."
            }
            if ($Quiet) {
                throw $detail
            }
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show(
                $detail + "`n`nUsageApp was not removed. Open it, resolve or disconnect Claude monitoring, and try again.",
                "UsageApp Native uninstall",
                [System.Windows.MessageBoxButton]::OK,
                [System.Windows.MessageBoxImage]::Warning) | Out-Null
            exit 2
        }
    }
    finally {
        Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    }
}

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
    throw "UsageApp Native is still running, so uninstall stopped without removing files."
}

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

$startupCommand = $null
try {
    $startupCommand = Get-ItemPropertyValue -Path $runKey -Name "UsageAppNative" -ErrorAction Stop
}
catch { }
$expectedStartup = '"' + $destinationExe + '" --background'
if ($startupCommand -is [string] -and
    [string]::Equals($startupCommand.Trim(), $expectedStartup,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Remove-ItemProperty -Path $runKey -Name "UsageAppNative" -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}
Remove-Item -Path $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
