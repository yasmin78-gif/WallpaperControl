<#
.SYNOPSIS
Tests the installer with an isolated application identity and registry key.
.PARAMETER InnoCompiler
Path to the Inno Setup compiler.
.PARAMETER PublishDir
An existing self-contained x64 publish directory to package for testing.
.OUTPUTS
Installation logs under artifacts/installer/test-<unique identifier>.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InnoCompiler,
    [Parameter(Mandatory)][string]$PublishDir
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content (Join-Path $repo 'WallpaperControl/WallpaperControl.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$id = 'WCTest.' + [guid]::NewGuid().ToString('N')
$testRoot = Join-Path $repo "artifacts/installer/test-$id"
$installDir = Join-Path $testRoot 'Installed App'
$registryPath = "Software\$id"
$registryProviderPath = "HKCU:\$registryPath"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${id}_is1"
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$id.lnk"
New-Item -ItemType Directory -Force $testRoot | Out-Null

function Invoke-TestProcess {
    <# .SYNOPSIS Runs a silent setup or uninstall and requires successful completion. #>
    param([string]$Path, [string[]]$Arguments)
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Process failed with exit code $($process.ExitCode): $Path" }
}

function Assert-Installer {
    <# .SYNOPSIS Reports a successful installer assertion or stops the test. #>
    param([bool]$Condition, [string]$Message)
    if (!$Condition) { throw $Message }
    Write-Output "PASS: $Message"
}

$setup = Join-Path $testRoot "WallpaperControl-$version-Setup-x64.exe"
$uninstall = Join-Path $installDir 'unins000.exe'
$commonArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCLOSEAPPLICATIONS', '/TASKS=""', '/LANG=german', "/DIR=`"$installDir`"")
try {
    & $InnoCompiler /Q "/DAppVersion=$version" "/DPublishDir=$([IO.Path]::GetFullPath($PublishDir))" `
        "/DOutputDir=$testRoot" "/DInstallerAppId=$id" "/DProductName=$id" "/DStartupKey=$registryPath" `
        (Join-Path $PSScriptRoot 'WallpaperControl.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }

    Invoke-TestProcess $setup ($commonArgs + "/LOG=`"$testRoot/fresh.log`"")
    Assert-Installer (Test-Path $uninstallKey) 'Fresh installation registers an uninstaller.'
    Assert-Installer (Test-Path $shortcut) 'Start menu shortcut is created.'
    Assert-Installer (!(Test-Path $registryProviderPath)) 'Fresh installation leaves autostart disabled.'
    $installedExe = Join-Path $installDir 'WallpaperControl.exe'
    Assert-Installer ((Get-Item $installedExe).VersionInfo.FileVersion -eq "$version.0") 'Installed executable has the expected version.'
    foreach ($file in Get-ChildItem -LiteralPath $PublishDir -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($PublishDir), $file.FullName)
        Assert-Installer ((Get-FileHash $file.FullName).Hash -eq (Get-FileHash (Join-Path $installDir $relative)).Hash) "Installed payload matches: $relative"
    }

    New-Item $registryProviderPath -Force | Out-Null
    New-ItemProperty $registryProviderPath -Name WallpaperControl -Value '"C:\Old Portable Copy\WallpaperControl.exe" --tray' -PropertyType String -Force | Out-Null
    Invoke-TestProcess $setup ($commonArgs + "/LOG=`"$testRoot/upgrade.log`"")
    $expected = '"' + $installedExe.Replace('/', '\') + '" --tray'
    Assert-Installer ((Get-ItemPropertyValue $registryProviderPath WallpaperControl) -eq $expected) 'Upgrade redirects enabled autostart to the installed executable.'
    Invoke-TestProcess $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testRoot/uninstall.log`"")
    Assert-Installer (!(Test-Path $installedExe)) 'Uninstall removes the installed executable.'
    Assert-Installer (!(Test-Path $shortcut)) 'Uninstall removes its shortcut.'
    Assert-Installer (!(Test-Path $uninstallKey)) 'Uninstall removes its Windows registration.'
    Assert-Installer ((Get-ItemProperty $registryProviderPath).PSObject.Properties.Name -notcontains 'WallpaperControl') 'Uninstall removes its own autostart command.'

    Invoke-TestProcess $setup ($commonArgs + "/LOG=`"$testRoot/reinstall.log`"")
    $otherCommand = '"C:\Other Copy\WallpaperControl.exe" --tray'
    New-ItemProperty $registryProviderPath -Name WallpaperControl -Value $otherCommand -PropertyType String -Force | Out-Null
    Invoke-TestProcess $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testRoot/uninstall-other-copy.log`"")
    Assert-Installer ((Get-ItemPropertyValue $registryProviderPath WallpaperControl) -eq $otherCommand) 'Uninstall preserves autostart redirected to another copy.'
    Write-Output "Installer tests passed. Logs: $testRoot"
}
finally {
    if (Test-Path $uninstall) { Invoke-TestProcess $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
    # Only this invocation's unique test registry key is removed.
    if (Test-Path $registryProviderPath) { Remove-Item -LiteralPath $registryProviderPath -Recurse -Force }
}
