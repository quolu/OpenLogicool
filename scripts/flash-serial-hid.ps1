#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^USB\\VID_1B4F&PID_9206\\[^\\]+$')]
    [string]$ExpectedDeviceInstanceId,

    [string]$EvidencePath,

    [switch]$WaitForReset
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SerialHidFlashTarget.psm1') -Force

$fqbn = 'SparkFun:avr:promicro:cpu=16MHzatmega32U4'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $env:LOCALAPPDATA 'OpenLogicool\ArduinoCli\1.5.1\arduino-cli.exe'
$configFile = Join-Path $env:LOCALAPPDATA 'OpenLogicool\Arduino\config\arduino-cli.yaml'
$buildPath = Join-Path $env:LOCALAPPDATA 'OpenLogicool\Arduino\build\OpenLogicool.SerialHid'
$sketchPath = Join-Path $repositoryRoot 'firmware\OpenLogicool.SerialHid'

function Get-ExactTarget {
    $targets = @(Get-PnpDevice -PresentOnly | Where-Object {
        $_.InstanceId -ieq $ExpectedDeviceInstanceId -and
        $_.Class -eq 'USB' -and
        $_.Status -eq 'OK'
    })
    if ($targets.Count -ne 1) {
        throw "Expected SparkFun Pro Micro target was not uniquely present: $ExpectedDeviceInstanceId (count=$($targets.Count))"
    }
    return $targets[0]
}

function Get-TargetDescendants {
    $targetContainer = (Get-PnpDeviceProperty `
        -InstanceId $ExpectedDeviceInstanceId `
        -KeyName 'DEVPKEY_Device_ContainerId' `
        -ErrorAction Stop).Data
    $matching = @(Get-PnpDevice -PresentOnly | Where-Object {
        $_.InstanceId -match 'VID_1B4F&PID_9206'
    })
    return @($matching | Where-Object {
        $container = (Get-PnpDeviceProperty `
            -InstanceId $_.InstanceId `
            -KeyName 'DEVPKEY_Device_ContainerId' `
            -ErrorAction Stop).Data
        $container -eq $targetContainer
    })
}

function Get-TargetPort {
    $ports = @(Get-TargetDescendants | Where-Object Class -eq 'Ports')
    if ($ports.Count -ne 1) {
        throw "Target CDC serial interface was not unique (count=$($ports.Count))."
    }
    if ($ports[0].FriendlyName -notmatch '\((COM\d+)\)$') {
        throw "Target CDC serial interface did not expose a COM port: $($ports[0].FriendlyName)"
    }
    return $Matches[1]
}

function Get-EnumerationSnapshot {
    $target = Get-ExactTarget
    $children = @(Get-TargetDescendants)
    [ordered]@{
        targetInstanceId = $target.InstanceId
        targetStatus = $target.Status
        targetFriendlyName = $target.FriendlyName
        cdc = @($children | Where-Object Class -eq 'Ports' | ForEach-Object {
            [ordered]@{ instanceId = $_.InstanceId; friendlyName = $_.FriendlyName; status = $_.Status }
        })
        keyboard = @($children | Where-Object Class -eq 'Keyboard' | ForEach-Object {
            [ordered]@{ instanceId = $_.InstanceId; friendlyName = $_.FriendlyName; status = $_.Status }
        })
        mouse = @($children | Where-Object Class -eq 'Mouse' | ForEach-Object {
            [ordered]@{ instanceId = $_.InstanceId; friendlyName = $_.FriendlyName; status = $_.Status }
        })
        hid = @($children | Where-Object Class -eq 'HIDClass' | ForEach-Object {
            [ordered]@{ instanceId = $_.InstanceId; friendlyName = $_.FriendlyName; status = $_.Status }
        })
    }
}

function Wait-TargetBootloader {
    $locations = @((Get-PnpDeviceProperty -InstanceId $ExpectedDeviceInstanceId -KeyName 'DEVPKEY_Device_LocationPaths').Data)
    Write-Host '書込み待機中です。USBを抜かず、基板の「RST」と「GND」の2点を金属で一瞬つなぎ、0.75秒以内にもう一度つないでください。'
    $deadline = [DateTime]::UtcNow.AddMinutes(30)
    do {
        $candidates = @(Get-PnpDevice -PresentOnly -Class Ports | ForEach-Object {
            [pscustomobject]@{
                InstanceId = $_.InstanceId
                FriendlyName = $_.FriendlyName
                Status = $_.Status
                LocationPaths = @((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_LocationPaths').Data)
            }
        })
        $bootPort = Find-SerialHidBootloaderPort -LocationPaths $locations -Candidates $candidates
        if ($bootPort) { return $bootPort }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw '同じNanoのdouble-resetを確認できませんでした。書き込んでいません。'
}

$before = Get-EnumerationSnapshot
$runtimePort = Get-TargetPort

& pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'build-serial-hid.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Serial HID firmware build failed.' }

$hexPath = Join-Path $buildPath 'OpenLogicool.SerialHid.ino.hex'
if (-not (Test-Path -LiteralPath $hexPath)) {
    throw "Compiled firmware hex was not found: $hexPath"
}
$hexSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $hexPath).Hash.ToLowerInvariant()

$bootloaderEntry = 'standard-1200-baud'
$uploadPort = $runtimePort
$uploadProperties = @()
if ($WaitForReset) {
    $bootloaderEntry = 'manual-double-reset'
    $uploadPort = Wait-TargetBootloader
    $uploadProperties = @('--upload-property', 'upload.use_1200bps_touch=false', '--upload-property', 'upload.wait_for_upload_port=false')
}
& $cli upload --verbose --fqbn $fqbn --port $uploadPort --build-path $buildPath --verify --config-file $configFile @uploadProperties $sketchPath
if ($LASTEXITCODE -ne 0) { throw 'Serial HID firmware upload failed.' }

$deadline = [DateTime]::UtcNow.AddSeconds(20)
$after = $null
do {
    Start-Sleep -Milliseconds 250
    try {
        $candidate = Get-EnumerationSnapshot
        if ($candidate.cdc.Count -eq 1 -and $candidate.keyboard.Count -eq 1 -and $candidate.mouse.Count -eq 1) {
            $after = $candidate
            break
        }
    }
    catch {
        # 1200-baud touchからruntime再列挙までの一時的な不在だけ待つ。
    }
} while ([DateTime]::UtcNow -lt $deadline)

if ($null -eq $after) {
    throw 'Flash後20秒以内に同じdevice instanceのCDC＋keyboard＋mouse再列挙が成立しませんでした。'
}

$result = [ordered]@{
    schema = 'openlogicool.serial-hid.flash-evidence.v1'
    capturedAtUtc = [DateTime]::UtcNow.ToString('O')
    expectedDeviceInstanceId = $ExpectedDeviceInstanceId
    fqbn = $fqbn
    transientRuntimePort = $runtimePort
    firmwareHexSha256 = $hexSha256
    bootloaderEntry = $bootloaderEntry
    before = $before
    uploadVerified = $true
    after = $after
}

$json = $result | ConvertTo-Json -Depth 8
if ($EvidencePath) {
    $resolvedEvidencePath = [System.IO.Path]::GetFullPath($EvidencePath, $repositoryRoot)
    $evidenceDirectory = Split-Path -Parent $resolvedEvidencePath
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    [System.IO.File]::WriteAllText($resolvedEvidencePath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}
$json
