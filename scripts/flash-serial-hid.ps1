#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^USB\\VID_1B4F&PID_9206\\[^\\]+$')]
    [string]$ExpectedDeviceInstanceId,

    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

function Enter-LegacyWatchdogBootloader {
    # 旧firmwareのwatchdog給餌を、Nano一台のHID解放失敗経路で止める。
    $locations = @((Get-PnpDeviceProperty -InstanceId $ExpectedDeviceInstanceId -KeyName 'DEVPKEY_Device_LocationPaths').Data)
    $hid = @(Get-TargetDescendants | Where-Object { $_.Class -eq 'HIDClass' -and $_.InstanceId -like 'USB\*' })
    if ($hid.Count -ne 1) { throw 'NanoのHID interfaceを一意に選べません。' }
    $script:legacyHidInterface = $hid[0].InstanceId
    & pnputil.exe /disable-device $script:legacyHidInterface /force | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'NanoのHID interfaceを一時停止できません。' }
    $script:legacyHidDisabled = $true
    $vectors = Get-Content -Raw (Join-Path $sketchPath 'protocol-v1-golden-vectors.json') | ConvertFrom-Json
    $helloHex = ($vectors.vectors | Where-Object name -eq 'hello-baseline-capabilities').frameHex -replace '\s', ''
    $hello = [Convert]::FromHexString($helloHex)
    $port = [IO.Ports.SerialPort]::new($runtimePort, 1200, [IO.Ports.Parity]::None, 8, [IO.Ports.StopBits]::One)
    $port.DtrEnable = $true
    $port.WriteTimeout = 1000
    try {
        $port.Open()
        $port.Write($hello, 0, $hello.Length)
        Start-Sleep -Milliseconds 50
    }
    finally { $port.Dispose() }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $bootPorts = @(Get-PnpDevice -PresentOnly -Class Ports | Where-Object {
            $_.InstanceId -like 'USB\VID_1B4F&PID_9205\*' -and $_.Status -eq 'OK'
        } | Where-Object {
            $bootLocations = @((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_LocationPaths').Data)
            @($bootLocations | Where-Object { $_ -in $locations }).Count -gt 0
        })
        if ($bootPorts.Count -gt 1) { throw '同じ物理接続のbootloaderが複数あります。' }
        if ($bootPorts.Count -eq 1 -and $bootPorts[0].FriendlyName -match '\((COM\d+)\)$') { return $Matches[1] }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw '同じNanoのbootloaderをソフトから起動できませんでした。書き込んでいません。'
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
$runtimeVersion = $null
$hostExecutable = Join-Path $repositoryRoot 'artifacts\development\OpenLogicool\OpenLogicool.Host.exe'
if (Test-Path -LiteralPath $hostExecutable) {
    $connectionJson = & $hostExecutable serial-hid-test --device-id $before.cdc[0].instanceId --repeat 1
    if ($LASTEXITCODE -eq 0) { $runtimeVersion = ($connectionJson | ConvertFrom-Json).FirmwareVersion }
}
$script:legacyHidInterface = $null
$script:legacyHidDisabled = $false
try {
    $uploadPort = $runtimePort
    $uploadProperties = @()
    if ($runtimeVersion -eq '1.1.3') {
        $bootloaderEntry = 'legacy-watchdog-release-reset'
        Write-Output '旧firmware 1.1.3のwatchdogと1200-baud resetの衝突を回復します。Nano一台のHIDを一時停止し、書込み後に戻します。'
        $uploadPort = Enter-LegacyWatchdogBootloader
        $uploadProperties = @('--upload-property', 'upload.use_1200bps_touch=false', '--upload-property', 'upload.wait_for_upload_port=false')
    }
    & $cli upload --verbose --fqbn $fqbn --port $uploadPort --build-path $buildPath --verify --config-file $configFile @uploadProperties $sketchPath
    if ($LASTEXITCODE -ne 0) { throw 'Serial HID firmware upload failed.' }
}
finally {
    if ($script:legacyHidDisabled) {
        & pnputil.exe /enable-device $script:legacyHidInterface | Out-Host
        if ($LASTEXITCODE -ne 0 -and (Get-PnpDevice -InstanceId $script:legacyHidInterface).Status -ne 'OK') {
            throw 'NanoのHID interfaceを有効へ戻せませんでした。'
        }
    }
}

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
    runtimeFirmwareVersionBefore = $runtimeVersion
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
