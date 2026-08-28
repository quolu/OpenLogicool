[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Build Tools vswhere.exe was not found.'
}
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw 'Visual C++ Build Tools were not found.'
}

$temporaryDirectory = Join-Path $env:TEMP "openlogicool-serial-hid-firmware-tests-$PID"
$resolvedTempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
$resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
if (-not $resolvedTemporaryDirectory.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not ([System.IO.Path]::GetFileName($resolvedTemporaryDirectory)).StartsWith('openlogicool-serial-hid-firmware-tests-', [System.StringComparison]::Ordinal)) {
    throw "Refusing unsafe temporary directory: $resolvedTemporaryDirectory"
}
$executable = Join-Path $temporaryDirectory 'SerialHidFirmwareTests.exe'
New-Item -ItemType Directory -Force -Path $temporaryDirectory | Out-Null
try {
    $vsDevCmd = Join-Path $visualStudio 'Common7\Tools\VsDevCmd.bat'
    $testSource = Join-Path $repositoryRoot 'tests\OpenLogicool.SerialHid.FirmwareTests\SerialHidFirmwareTests.cpp'
    $protocolSource = Join-Path $repositoryRoot 'firmware\OpenLogicool.SerialHid\ProtocolV1.cpp'
    $testInclude = Join-Path $repositoryRoot 'tests\OpenLogicool.SerialHid.FirmwareTests'
    $firmwareInclude = Join-Path $repositoryRoot 'firmware\OpenLogicool.SerialHid'
    $compile = "cd /d `"$temporaryDirectory`" && call `"$vsDevCmd`" -arch=x64 -host_arch=x64 >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /I`"$testInclude`" /I`"$firmwareInclude`" `"$testSource`" `"$protocolSource`" /Fe:`"$executable`""
    & cmd.exe /d /c $compile
    if ($LASTEXITCODE -ne 0) { throw 'Serial HID firmware native test build failed.' }

    $vectorsPath = Join-Path $firmwareInclude 'protocol-v1-golden-vectors.json'
    $vectors = Get-Content -Raw -LiteralPath $vectorsPath | ConvertFrom-Json
    $kindByName = @{
        Hello = 1; Ready = 2; SetState = 3; AllUp = 4; Heartbeat = 5; Ack = 6; Fault = 7; MouseDelta = 8
    }
    foreach ($vector in $vectors.vectors) {
        $compactFrame = [string]$vector.frameHex -replace '\s', ''
        $actual = (& $executable decode $compactFrame).Trim()
        if ($LASTEXITCODE -ne 0) { throw "firmware decoder failed: $($vector.name)" }
        $expectedPayload = ([string]$vector.payloadHex -replace '\s', '').ToUpperInvariant()
        $expected = "ok|$($kindByName[$vector.kind])|$($vector.sequence)|$expectedPayload"
        if ($actual -ne $expected) {
            throw "golden vector mismatch: $($vector.name) expected=$expected actual=$actual"
        }
    }

    $lease = (& $executable lease).Trim()
    if ($LASTEXITCODE -ne 0 -or $lease -ne 'lease|ok|150') {
        throw "firmware lease fake clock failed: $lease"
    }
    $faults = (& $executable faults).Trim()
    if ($LASTEXITCODE -ne 0 -or $faults -ne 'faults|ok|checksum|version') {
        throw "firmware decoder fault classification failed: $faults"
    }
    $mouseDelta = (& $executable mouse-delta).Trim()
    if ($LASTEXITCODE -ne 0 -or $mouseDelta -ne 'mouse-delta|ok|range|negotiation') {
        throw "firmware mouse delta contract failed: $mouseDelta"
    }
    $recovery = (& $executable recovery).Trim()
    if ($LASTEXITCODE -ne 0 -or $recovery -ne 'recovery|ok|1000') {
        throw "firmware recovery timer failed: $recovery"
    }
    Write-Output "Firmware native tests: $($vectors.vectors.Count) golden vectors, checksum/version faults, lease 150ms, mouse delta negotiation, recovery reset 1000ms"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
