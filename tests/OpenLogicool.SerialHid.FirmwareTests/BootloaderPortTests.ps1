#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $root 'scripts/SerialHidFlashTarget.psm1') -Force
$fixture = Get-Content (Join-Path $root 'fixtures/serial-hid/bootloader-port-history.v1.json') -Raw | ConvertFrom-Json

function Assert-Equal($Actual, $Expected, [string]$Name) {
    if ($Actual -cne $Expected) { throw "$Name : expected=$Expected actual=$Actual" }
    Write-Output "成功: $Name"
}

function Assert-Throws([scriptblock]$Action, [type]$Type, [string]$Name) {
    try { & $Action }
    catch {
        if ($_.Exception -isnot $Type) { throw }
        Write-Output "成功: $Name"
        return
    }
    throw "$Name : 例外が発生しませんでした。"
}

# 実録の機器識別子・接続口を使い、書込みモードでPresentになった状態を再生する。
$leonardo = [pscustomobject]@{
    InstanceId = $fixture.BootloaderInstanceId
    FriendlyName = $fixture.BootloaderFriendlyName
    Status = 'OK'
    LocationPaths = $fixture.BootloaderLocationPaths
}
$paths = $fixture.RuntimeLocationPaths
Assert-Equal (Find-SerialHidBootloaderPort $paths @($leonardo)) 'COM5' '同じ接続口の実録Leonardoを選ぶ'

$absent = $leonardo | Select-Object *
$absent.Status = 'Unknown'
Assert-Equal (Find-SerialHidBootloaderPort $paths @($absent)) $null '過去の非接続機器を選ばない'

$other = $leonardo | Select-Object *
$other.LocationPaths = @('PCIROOT(0)#USBROOT(0)#USB(99)')
Assert-Equal (Find-SerialHidBootloaderPort $paths @($other)) $null '別の物理接続口を選ばない'

$sparkfun = $leonardo | Select-Object *
$sparkfun.InstanceId = 'USB\VID_1B4F&PID_9205\BOOT'
$sparkfun.FriendlyName = 'SparkFun Pro Micro (COM9)'
Assert-Equal (Find-SerialHidBootloaderPort $paths @($sparkfun)) 'COM9' 'SparkFunの書込み機器も選ぶ'

$unknown = $leonardo | Select-Object *
$unknown.InstanceId = 'USB\VID_9999&PID_1234\UNKNOWN'
Assert-Throws { Find-SerialHidBootloaderPort $paths @($unknown) } ([NotSupportedException]) '同じ接続口でも未知の機器は拒否'
Assert-Throws { Find-SerialHidBootloaderPort $paths @($leonardo, $sparkfun) } ([InvalidOperationException]) '複数の書込み候補は拒否'

$runtime = $leonardo | Select-Object *
$runtime.InstanceId = 'USB\VID_1B4F&PID_9206&MI_00\RUNTIME'
Assert-Equal (Find-SerialHidBootloaderPort $paths @($runtime)) $null '通常動作中の機器へ書き込まない'

$enumerating = $leonardo | Select-Object *
$enumerating.FriendlyName = 'Arduino Leonardo'
Assert-Equal (Find-SerialHidBootloaderPort $paths @($enumerating)) $null 'COM番号の確定前は待つ'
