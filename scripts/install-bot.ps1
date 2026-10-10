# 本体とBotの導入はinstall-development-app.ps1。Botだけの更新はこのスクリプトを単独で使う。
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$botRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/development/OpenLogicool.Bot'))
$version = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$versionDirectory = Join-Path $botRoot $version
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($botRoot.ToUpperInvariant()))).Substring(0, 20)
$mutex = [Threading.Mutex]::new($false, "Local\OpenLogicool.Bot.Versions.$hash")
$installMark = $null
try {
    # フォルダーの作成から印を開くまでを、別の導入処理の掃除と排他にする。
    try { [void]$mutex.WaitOne() } catch [Threading.AbandonedMutexException] { }
    try {
        New-Item -ItemType Directory -Path $versionDirectory -Force | Out-Null
        $installMark = [IO.FileStream]::new((Join-Path $versionDirectory ('.running-install-' + [Guid]::NewGuid().ToString('N'))),
            [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read, 1, [IO.FileOptions]::DeleteOnClose)
    }
    finally { $mutex.ReleaseMutex() }

    & dotnet publish (Join-Path $repositoryRoot 'src/OpenLogicool.Host/OpenLogicool.Host.csproj') `
        --configuration Release --output $versionDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Botのpublishに失敗しました。current.txtは更新しません。' }

    foreach ($file in @('OpenLogicool.Host.exe', 'OpenLogicool.Host.dll', 'OpenLogicool.Host.runtimeconfig.json', 'OpenLogicool.Host.deps.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $versionDirectory $file) -PathType Leaf)) {
            throw "Botの必須ファイルがありません: $file"
        }
    }
    $selfTest = & (Join-Path $versionDirectory 'OpenLogicool.Host.exe') bot-worker --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Botの設定・protocolの確認に失敗しました。current.txtは更新しません。' }
    if (($selfTest | ConvertFrom-Json).Protocol -ne 1) { throw 'Botのprotocolが導入処理と一致しません。current.txtは更新しません。' }

    # 掃除に失敗しても、手入力の監視の導入は済ませる。
    & (Join-Path $PSScriptRoot 'install-user-input-watch.ps1')

    # 本体のcurrent読取と実行印の作成も、このmutexで一続きにする。
    try { [void]$mutex.WaitOne() } catch [Threading.AbandonedMutexException] { }
    try {
        $currentPath = Join-Path $botRoot 'current.txt'
        $previous = if (Test-Path -LiteralPath $currentPath) { [IO.File]::ReadAllText($currentPath).Trim() } else { '' }
        $encoding = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText((Join-Path $botRoot 'previous.txt.tmp'), $previous, $encoding)
        [IO.File]::Move((Join-Path $botRoot 'previous.txt.tmp'), (Join-Path $botRoot 'previous.txt'), $true)
        [IO.File]::WriteAllText("$currentPath.tmp", $version, $encoding)
        [IO.File]::Move("$currentPath.tmp", $currentPath, $true)

        $prefix = $botRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($directory in Get-ChildItem -LiteralPath $botRoot -Directory) {
            if ($directory.Name -in @($version, $previous)) { continue }
            $targetPath = [IO.Path]::GetFullPath($directory.FullName)
            if (-not $targetPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
                ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Botの削除対象が通常の版フォルダーではありません: $targetPath"
            }
            try {
                $inUse = $false
                foreach ($mark in Get-ChildItem -LiteralPath $targetPath -Filter '.running-*' -Force -File) {
                    try {
                        $probe = [IO.FileStream]::new($mark.FullName, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                        $probe.Dispose()
                        Remove-Item -LiteralPath $mark.FullName -Force
                    }
                    catch [IO.IOException] { $inUse = $true }
                }
                if ($inUse) { Write-Output "実行中のBotの版を保持します: $targetPath"; continue }
                Remove-Item -LiteralPath $targetPath -Recurse -Force
            }
            catch { throw "Botの古い版 '$($directory.Name)' を削除できません: $($_.Exception.Message)" }
        }
    }
    finally { $mutex.ReleaseMutex() }
}
finally {
    if ($null -ne $installMark) { $installMark.Dispose() }
    $mutex.Dispose()
}
Write-Output "Botの導入先: $versionDirectory"
Write-Output "戻し先の版: $previous"
