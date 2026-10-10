[CmdletBinding()]
param(
    # 登録を消す。
    [switch]$Remove
)

# ゲームが管理者権限で動いている間、通常権限のOpenLogicoolにはキーボードとマウスの入力が見えない。
# 手入力の監視だけを管理者権限で動かすため、導入先の OpenLogicool.Host.exe user-input-watch を起動するタスクを登録する。
# 登録と削除にだけ昇格が要り、UACの確認が1回出る。Botは登録済みのタスクを確認なしで起動する。
# 戻し方: このスクリプトを -Remove で実行する。

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$taskPath = '\OpenLogicool\'
$taskName = 'UserInputWatch'
$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$applicationDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\development\OpenLogicool'))
$launcherPath = Join-Path $applicationDirectory 'OpenLogicool.Launcher.exe'
$hostPath = Join-Path $applicationDirectory 'OpenLogicool.Host.exe'
# 画面に窓を出さないよう、窓を持たないLauncherからHostを起動する。
$taskArguments = "--host `"$hostPath`" user-input-watch"

function Get-WatchTask {
    Get-ScheduledTask -TaskPath $taskPath -TaskName $taskName -ErrorAction SilentlyContinue
}

function Test-WatchTask {
    $task = Get-WatchTask
    if (-not $task) { return $false }
    $action = @($task.Actions)[0]
    return $action.Execute -eq $launcherPath -and $action.Arguments -eq $taskArguments -and "$($task.Principal.RunLevel)" -eq 'Highest'
}

if (-not $Remove -and (Test-WatchTask)) {
    Write-Output "手入力の監視のタスクは登録済みです: $taskPath$taskName"
    return
}
if ($Remove -and -not (Get-WatchTask)) {
    Write-Output "手入力の監視のタスクは登録されていません。"
    return
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdministrator = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    $argumentList = @('-NoProfile', '-File', "`"$PSCommandPath`"")
    if ($Remove) { $argumentList += '-Remove' }
    $elevated = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $argumentList -Verb RunAs -Wait -PassThru -WindowStyle Hidden
    if ($elevated.ExitCode -ne 0) {
        throw "手入力の監視のタスクを変更できませんでした: exit=$($elevated.ExitCode)"
    }
    if ($Remove) {
        if (Get-WatchTask) { throw '手入力の監視のタスクが残っています。' }
        Write-Output "手入力の監視のタスクを削除しました。"
    }
    else {
        if (-not (Test-WatchTask)) { throw '手入力の監視のタスクが登録されていません。' }
        Write-Output "手入力の監視のタスクを登録しました: $taskPath$taskName"
    }
    return
}

if ($Remove) {
    Unregister-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Confirm:$false
    return
}

$action = New-ScheduledTaskAction -Execute $launcherPath -Argument $taskArguments -WorkingDirectory $applicationDirectory
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
# Botの実行は長く続くので、実行時間の上限を外す。前の監視が終わる前に次のBotが始まっても起動できるよう、並行を許す。
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances Parallel -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskPath $taskPath -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
