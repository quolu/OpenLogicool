[CmdletBinding()]
param(
    # 監視のプログラムとタスクを消す。
    [switch]$Remove,
    # 昇格した手順が使う。通常は指定しない。
    [string]$ElevatedSource,
    [string]$ElevatedSha256
)

# ゲームが管理者権限で動いている間、通常権限のOpenLogicoolにはキーボードとマウスの入力が見えない。
# 手入力の監視だけを行う OpenLogicool.InputWatch.exe を、管理者だけが書ける場所へ置き、管理者権限で起動するタスクを登録する。
# 管理者権限で動くのはこの実行ファイルだけで、Bot本体と開発版の置き場所は管理者権限で動かさない。
# 配置と登録にだけ昇格が要る。監視の中身が変わった時と、タスクが正しくない時だけ、UACの確認が1回出る。
# 戻し方: このスクリプトを -Remove で実行する。

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$taskPath = '\OpenLogicool\'
$taskName = 'UserInputWatch'
$executableName = 'OpenLogicool.InputWatch.exe'
$installDirectory = Join-Path $env:ProgramFiles 'OpenLogicool\InputWatch'
$installedExecutable = Join-Path $installDirectory $executableName
$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Get-WatchTask {
    Get-ScheduledTask -TaskPath $taskPath -TaskName $taskName -ErrorAction SilentlyContinue
}

# タスクは、置き場所の実行ファイルだけを引数なしで起動する。利用者が書ける場所のパスを実行させない。
function Test-WatchTask {
    $task = Get-WatchTask
    if (-not $task) { return $false }
    $actions = @($task.Actions)
    if ($actions.Count -ne 1) { return $false }
    return $actions[0].Execute -eq $installedExecutable -and [string]::IsNullOrEmpty($actions[0].Arguments) `
        -and "$($task.Principal.RunLevel)" -eq 'Highest'
}

# 置き場所と中のファイルを、管理者でない利用者が書き換えられないことを確かめる。
function Assert-AdministratorOnlyWrite([string]$path) {
    # SYSTEM・Administrators・TrustedInstaller・CREATOR OWNER。
    $trusted = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464', 'S-1-3-0')
    # 書き込み・削除・権限の変更にあたる権利（個別の権利と、まとめて与える権利の両方）。
    $writeMask = 0x2 -bor 0x4 -bor 0x10 -bor 0x40 -bor 0x100 -bor 0x10000 -bor 0x40000 -bor 0x80000 -bor 0x40000000 -bor 0x10000000
    $acl = Get-Acl -LiteralPath $path
    $owner = $acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value
    if ($trusted -notcontains $owner) {
        throw "監視の置き場所の所有者が管理者ではありません: $path owner=$owner"
    }
    foreach ($rule in $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) { continue }
        if ($trusted -contains $rule.IdentityReference.Value) { continue }
        $rights = [int64][BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$rule.FileSystemRights), 0)
        if (($rights -band $writeMask) -ne 0) {
            throw "監視の置き場所を管理者でない利用者が書き換えられます: $path $($rule.IdentityReference.Value) $($rule.FileSystemRights)"
        }
    }
}

function Test-Installed([string]$expectedSha256) {
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) { return $false }
    if ((Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash -ne $expectedSha256) { return $false }
    if (@(Get-ChildItem -LiteralPath $installDirectory -Force).Count -ne 1) { return $false }
    Assert-AdministratorOnlyWrite $installDirectory
    Assert-AdministratorOnlyWrite $installedExecutable
    return Test-WatchTask
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdministrator = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($ElevatedSource -or ($Remove -and $isAdministrator)) {
    # ここからは昇格した手順。
    if (-not $isAdministrator) { throw '昇格した手順は管理者として実行します。' }
    if (Get-WatchTask) { Unregister-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Confirm:$false }
    if (Test-Path -LiteralPath $installDirectory) { Remove-Item -LiteralPath $installDirectory -Recurse -Force }
    if ($Remove) { return }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $ElevatedSource -Destination $installedExecutable
    # 昇格の確認の前に計算した値と比べ、確認の後に差し替えられたファイルを置かない。
    if ((Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash -ne $ElevatedSha256) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
        throw '置いた実行ファイルが、昇格の前に確かめたものと一致しません。'
    }
    Assert-AdministratorOnlyWrite $installDirectory
    Assert-AdministratorOnlyWrite $installedExecutable
    $selfTest = Start-Process -FilePath $installedExecutable -ArgumentList '--self-test' -Wait -PassThru
    if ($selfTest.ExitCode -ne 0) { throw "置いた実行ファイルで手入力の観測を始められません: exit=$($selfTest.ExitCode)" }

    $action = New-ScheduledTaskAction -Execute $installedExecutable -WorkingDirectory $installDirectory
    $principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
    # Botの実行は長く続くので、実行時間の上限を外す。前の監視が終わる前に次のBotが始まっても起動できるよう、並行を許す。
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances Parallel -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    return
}

function Invoke-Elevated([string[]]$extraArguments) {
    $argumentList = @('-NoProfile', '-File', "`"$PSCommandPath`"") + $extraArguments
    $elevated = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $argumentList -Verb RunAs -Wait -PassThru -WindowStyle Hidden
    if ($elevated.ExitCode -ne 0) { throw "昇格した手順が失敗しました: exit=$($elevated.ExitCode)" }
}

if ($Remove) {
    if (-not (Get-WatchTask) -and -not (Test-Path -LiteralPath $installDirectory)) {
        Write-Output '手入力の監視は導入されていません。'
        return
    }
    Invoke-Elevated @('-Remove')
    if ((Get-WatchTask) -or (Test-Path -LiteralPath $installDirectory)) { throw '手入力の監視が残っています。' }
    Write-Output '手入力の監視を削除しました。'
    return
}

# 実行時の部品を環境変数で差し替えられない単体の実行ファイルとして出力する。
# 出力の道具が vswhere.exe を名前だけで呼ぶため、その場所をPATHへ足す。
$visualStudioInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Test-Path -LiteralPath (Join-Path $visualStudioInstaller 'vswhere.exe'))) {
    throw '単体の実行ファイルの出力に要る Visual Studio Build Tools（C++ のビルドツール）が見つかりません。'
}
$env:PATH = "$visualStudioInstaller;$env:PATH"
$stagingDirectory = Join-Path $repositoryRoot 'artifacts\input-watch'
if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
& dotnet publish (Join-Path $repositoryRoot 'src\OpenLogicool.InputWatch\OpenLogicool.InputWatch.csproj') `
    --configuration Release --output $stagingDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed: OpenLogicool.InputWatch' }
$stagedExecutable = Join-Path $stagingDirectory $executableName
$stagedSha256 = (Get-FileHash -LiteralPath $stagedExecutable -Algorithm SHA256).Hash

if (Test-Installed $stagedSha256) {
    Write-Output "手入力の監視は導入済みです: $installedExecutable"
    return
}

Invoke-Elevated @('-ElevatedSource', "`"$stagedExecutable`"", '-ElevatedSha256', $stagedSha256)
if (-not (Test-Installed $stagedSha256)) { throw '手入力の監視を導入できませんでした。' }
Write-Output "手入力の監視を導入しました: $installedExecutable"
