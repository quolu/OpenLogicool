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
    Remove-Item -LiteralPath $applicationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $applicationDirectory | Out-Null

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
