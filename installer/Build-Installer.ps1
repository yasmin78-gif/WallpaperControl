<#
.SYNOPSIS
Publishes Wallpaper Control and builds its self-contained Windows x64 installer.
.PARAMETER InnoCompiler
Optional path to ISCC.exe from Inno Setup 6.3 or later.
.OUTPUTS
The setup executable and its SHA-256 checksum in artifacts/installer.
#>
[CmdletBinding()]
param([string]$InnoCompiler)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'WallpaperControl/WallpaperControl.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unsupported release version: $version" }

if (!$InnoCompiler) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $InnoCompiler = $compilerCommand.Source }
    else {
        $candidates = @(
            "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
            "$env:ProgramFiles/Inno Setup 7/ISCC.exe",
            "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe",
            "$env:LOCALAPPDATA/Programs/Inno Setup 7/ISCC.exe"
        )
        $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Install Inno Setup or pass -InnoCompiler with the path to ISCC.exe.'
}

$output = Join-Path $repo 'artifacts/installer'
# A fresh publish directory prevents obsolete files from entering later releases.
$publish = Join-Path $output ('publish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $publish | Out-Null
try {
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:PublishReadyToRun=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
    $exe = Join-Path $publish 'WallpaperControl.exe'
    if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "$version.0") {
        throw 'Published executable version does not match the project version.'
    }
    & $InnoCompiler "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" `
        (Join-Path $PSScriptRoot 'WallpaperControl.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $LASTEXITCODE" }
    $installer = Join-Path $output "WallpaperControl-$version-Setup-x64.exe"
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
    Get-Item -LiteralPath $installer, "$installer.sha256"
}
finally {
    # Only remove the unique temporary directory created by this invocation.
    $resolvedPublish = [IO.Path]::GetFullPath($publish)
    $resolvedOutput = [IO.Path]::GetFullPath($output).TrimEnd('\') + '\'
    if (!$resolvedPublish.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a publish directory outside the installer output folder.'
    }
    if (Test-Path -LiteralPath $resolvedPublish) { Remove-Item -LiteralPath $resolvedPublish -Recurse -Force }
}
