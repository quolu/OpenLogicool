[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$applicationDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'development\OpenLogicool'))
$artifactPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $applicationDirectory.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Development application directory escaped the repository artifact root: $applicationDirectory"
}

if (Test-Path -LiteralPath $applicationDirectory) {
    # 起動用shell等が作業フォルダを保持していても、更新するファイルだけを入れ替える。
    $applicationPrefix = $applicationDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($entry in Get-ChildItem -LiteralPath $applicationDirectory -Force) {
        $entryPath = [System.IO.Path]::GetFullPath($entry.FullName)
        if (-not $entryPath.StartsWith($applicationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "更新対象が開発版フォルダの外にあります: $entryPath"
        }
        Remove-Item -LiteralPath $entryPath -Recurse -Force
    }
}
else {
    New-Item -ItemType Directory -Path $applicationDirectory | Out-Null
}

$projects = @(
    'src\OpenLogicool.Host\OpenLogicool.Host.csproj',
    'src\OpenLogicool.Watchdog\OpenLogicool.Watchdog.csproj',
    'src\OpenLogicool.Launcher\OpenLogicool.Launcher.csproj'
)

foreach ($project in $projects) {
    & dotnet publish (Join-Path $repositoryRoot $project) `
        --configuration Release `
        --output $applicationDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed: $project"
    }
}

$requiredFiles = @(
    'OpenLogicool.Launcher.exe',
    'OpenLogicool.Launcher.runtimeconfig.json',
    'OpenLogicool.Host.exe',
    'OpenLogicool.Host.runtimeconfig.json',
    'OpenLogicool.Watchdog.exe',
    'OpenLogicool.Watchdog.runtimeconfig.json'
)
foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $applicationDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Development application layout is incomplete: $requiredPath"
    }
}

# 管理者権限で動くゲームの手入力を見るための監視。管理者だけが書ける場所へ置く。
# 監視の中身が変わった時と、タスクが正しくない時だけ、UACの確認が1回出る。
& (Join-Path $PSScriptRoot 'install-user-input-watch.ps1')

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop 'OpenLogicool.lnk'
$launcherPath = Join-Path $applicationDirectory 'OpenLogicool.Launcher.exe'
$hostPath = Join-Path $applicationDirectory 'OpenLogicool.Host.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $launcherPath
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $applicationDirectory
$shortcut.IconLocation = "$hostPath,0"
$shortcut.WindowStyle = 1
$shortcut.Save()

Write-Output "Development application: $applicationDirectory"
Write-Output "Desktop shortcut: $shortcutPath"
