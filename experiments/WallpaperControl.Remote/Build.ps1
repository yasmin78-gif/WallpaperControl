[CmdletBinding()]
param(
    [string]$AndroidSdk = 'C:\Program Files (x86)\Android\android-sdk',
    [string]$JavaSdk = 'C:\Program Files\Android\openjdk\jdk-21.0.8'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo 'artifacts/remote-phase01'
New-Item -ItemType Directory -Force $output, (Join-Path $PSScriptRoot 'out') | Out-Null
Push-Location $PSScriptRoot
try {
    foreach ($project in @('Host', 'Baseline')) {
        & dotnet publish "$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:RuntimeFrameworkVersion=10.0.15 -o "$output/$project" *> "out/publish-$project.log"
        if ($LASTEXITCODE -ne 0) { throw "Publish $project failed; inspect out/publish-$project.log" }
    }
    Push-Location Android
    try {
        & dotnet build Android.csproj -c Debug "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" *> ../out/android-build.log
        if ($LASTEXITCODE -ne 0) { throw 'Android build failed; inspect out/android-build.log' }
        Copy-Item -LiteralPath 'bin/Debug/net11.0-android/android-arm64/de.wallpapercontrol.remote.experiment-Signed.apk' -Destination "$output/WallpaperControl.Remote.Experiment.apk"
    } finally { Pop-Location }
    Get-ChildItem $output -File -Recurse | Where-Object Name -ne 'manifest.json' | ForEach-Object { [pscustomobject]@{ File=$_.FullName.Substring($output.Length+1); Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } | ConvertTo-Json | Set-Content "$output/manifest.json" -Encoding utf8
    Write-Output "Artifacts: $output"
} finally { Pop-Location }
