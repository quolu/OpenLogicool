[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$cliVersion = '1.5.1'
$cliSha256 = 'fabe42e0eb04d00e776a66178299ff95a46c623dbc260f997e58fd514853dd40'
$cliExecutableSha256 = '1017de89179c3167e6b8a38ca6cc4091fa69a3cf97aafa7810f01423003f5571'
$sparkFunCore = 'SparkFun:avr@1.1.13'
$arduinoAvrCore = 'arduino:avr@1.8.8'
$sparkFunCoreUrl = 'https://github.com/sparkfun/Arduino_Boards/raw/main/IDE_Board_Manager/sparkfunboards.1.1.13.tar.bz2'
$sparkFunCoreSha256 = 'd7af391cafc5e16830cac7c13484ef62765dd7a36aaba5f25020ce3c39617115'
$arduinoAvrCoreUrl = 'https://downloads.arduino.cc/cores/staging/avr-1.8.8.tar.bz2'
$arduinoAvrCoreSha256 = 'a234c2a43dcd01ce54be665806f183b8e6ec4e966d2e3e0c3358b63023d6390c'
$avrGccUrl = 'https://downloads.arduino.cc/tools/avr-gcc-7.3.0-atmel3.6.1-arduino7-i686-w64-mingw32.zip'
$avrGccSha256 = 'a54f64755fff4cb792a1495e5defdd789902a2a3503982e81b898299cf39800e'
$avrGppExecutableSha256 = '098f5708a5d70e3abb6b504145b46c9320ef4b6d43b6b8f1c09b431e611d543b'
$fqbn = 'SparkFun:avr:promicro:cpu=16MHzatmega32U4'
$sparkFunIndex = 'https://raw.githubusercontent.com/sparkfun/Arduino_Boards/main/IDE_Board_Manager/package_sparkfun_index.json'
$cliUrl = "https://github.com/arduino/arduino-cli/releases/download/v$cliVersion/arduino-cli_${cliVersion}_Windows_64bit.zip"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sketchPath = Join-Path $repositoryRoot 'firmware\OpenLogicool.SerialHid'
$cacheRoot = Join-Path $env:LOCALAPPDATA 'OpenLogicool\Arduino'
$cliRoot = Join-Path $env:LOCALAPPDATA "OpenLogicool\ArduinoCli\$cliVersion"
$cliArchive = Join-Path $cliRoot "arduino-cli_${cliVersion}_Windows_64bit.zip"
$cli = Join-Path $cliRoot 'arduino-cli.exe'
$configDirectory = Join-Path $cacheRoot 'config'
$configFile = Join-Path $configDirectory 'arduino-cli.yaml'
$buildPath = Join-Path $cacheRoot 'build\OpenLogicool.SerialHid'

function Assert-ArchiveChecksum {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Invoke-WebRequest -Uri $Url -OutFile $Path
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "Package archive checksum mismatch: $Path ($actual)"
    }
}

function Assert-FileChecksum {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "Installed executable checksum mismatch: $Path ($actual)"
    }
}

New-Item -ItemType Directory -Force -Path $cliRoot, $configDirectory, $buildPath | Out-Null

if (-not (Test-Path -LiteralPath $cliArchive)) {
    Invoke-WebRequest -Uri $cliUrl -OutFile $cliArchive
}
$actualCliSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $cliArchive).Hash.ToLowerInvariant()
if ($actualCliSha256 -ne $cliSha256) {
    throw "Arduino CLI archive checksum mismatch: $actualCliSha256"
}
if (-not (Test-Path -LiteralPath $cli)) {
    Expand-Archive -LiteralPath $cliArchive -DestinationPath $cliRoot
}
Assert-FileChecksum -Path $cli -ExpectedSha256 $cliExecutableSha256

$reportedVersion = & $cli version
if ($LASTEXITCODE -ne 0 -or $reportedVersion -notmatch "Version: $([regex]::Escape($cliVersion))(\s|$)") {
    throw "Pinned Arduino CLI $cliVersion did not start: $reportedVersion"
}

if (-not (Test-Path -LiteralPath $configFile)) {
    & $cli config init --dest-dir $configDirectory --overwrite
    if ($LASTEXITCODE -ne 0) { throw 'arduino-cli config init failed.' }
}
& $cli config set directories.data (Join-Path $cacheRoot 'data') --config-file $configFile
& $cli config set directories.downloads (Join-Path $cacheRoot 'downloads') --config-file $configFile
& $cli config set directories.user (Join-Path $cacheRoot 'user') --config-file $configFile
if (-not (Select-String -LiteralPath $configFile -SimpleMatch $sparkFunIndex -Quiet)) {
    & $cli config add board_manager.additional_urls $sparkFunIndex --config-file $configFile
}

$installedCoreList = & $cli core list --format json --config-file $configFile | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'arduino-cli core list failed.' }
$installedCoreIds = @($installedCoreList.platforms | ForEach-Object { "$($_.id)@$($_.installed_version)" })
$requiredCores = @($sparkFunCore, $arduinoAvrCore)
$missingCores = @($requiredCores | Where-Object { $_ -notin $installedCoreIds })
if ($missingCores.Count -gt 0) {
    & $cli core update-index --config-file $configFile
    if ($LASTEXITCODE -ne 0) { throw 'arduino-cli core update-index failed.' }
    foreach ($core in $missingCores) {
        & $cli core install $core --skip-post-install --config-file $configFile
        if ($LASTEXITCODE -ne 0) { throw "arduino-cli core install failed: $core" }
    }
}

$packageDownloads = Join-Path $cacheRoot 'downloads\packages'
New-Item -ItemType Directory -Force -Path $packageDownloads | Out-Null
Assert-ArchiveChecksum `
    -Path (Join-Path $packageDownloads 'sparkfunboards.1.1.13.tar.bz2') `
    -Url $sparkFunCoreUrl `
    -ExpectedSha256 $sparkFunCoreSha256
Assert-ArchiveChecksum `
    -Path (Join-Path $packageDownloads 'avr-1.8.8.tar.bz2') `
    -Url $arduinoAvrCoreUrl `
    -ExpectedSha256 $arduinoAvrCoreSha256
Assert-ArchiveChecksum `
    -Path (Join-Path $packageDownloads 'avr-gcc-7.3.0-atmel3.6.1-arduino7-i686-w64-mingw32.zip') `
    -Url $avrGccUrl `
    -ExpectedSha256 $avrGccSha256
Assert-FileChecksum `
    -Path (Join-Path $cacheRoot 'data\packages\arduino\tools\avr-gcc\7.3.0-atmel3.6.1-arduino7\bin\avr-g++.exe') `
    -ExpectedSha256 $avrGppExecutableSha256

& $cli compile `
    --clean `
    --fqbn $fqbn `
    --build-path $buildPath `
    --build-property 'build.usb_product=\"OpenLogicool\040Serial\040HID\"' `
    --build-property 'build.usb_manufacturer=\"OpenLogicool\"' `
    --warnings all `
    --config-file $configFile `
    $sketchPath
if ($LASTEXITCODE -ne 0) { throw 'Serial HID firmware compile failed.' }

Write-Output "Firmware build: $buildPath"
