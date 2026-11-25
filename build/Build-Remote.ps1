[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Dotnet11 = (Join-Path $env:LOCALAPPDATA 'WallpaperControl.BuildTools/dotnet11/dotnet.exe'),
    [string]$AndroidSdk = 'C:\Program Files (x86)\Android\android-sdk',
    [string]$JavaSdk = 'C:\Program Files\Android\openjdk\jdk-21.0.8'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/remote-production'
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Force $output,$logs | Out-Null
Push-Location $repo
try {
    if (!$SkipTests) {
        & pwsh -NoProfile -File ./Test.ps1 *> "$logs/regression.log"
        if ($LASTEXITCODE -ne 0) { throw 'Desktop regression failed' }
        & dotnet run --project WallpaperControl.Remote.Tests/WallpaperControl.Remote.Tests.csproj -c Release *> "$logs/remote-tests.log"
        if ($LASTEXITCODE -ne 0) { throw 'Remote integration tests failed' }
    }
    if (!(Test-Path -LiteralPath $Dotnet11)) { throw 'Stable .NET 11 + maui-android workload required. Pass -Dotnet11 pointing to the stable installation.' }
    $previousRoot = $env:DOTNET_ROOT
    try {
        $env:DOTNET_ROOT = Split-Path -Parent $Dotnet11
        Push-Location WallpaperControl.Remote.Android
        try {
            & $Dotnet11 restore WallpaperControl.Remote.Android.csproj *> "$logs/android-restore.log"
            if ($LASTEXITCODE -ne 0) { throw 'Android restore failed' }
        } finally { Pop-Location }
    } finally { $env:DOTNET_ROOT = $previousRoot }
    & "$PSScriptRoot/Generate-RemoteNotices.ps1"
    & dotnet publish WallpaperControl/WallpaperControl.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o "$output/desktop" *> "$logs/desktop-publish.log"
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
    $signing = Join-Path $env:LOCALAPPDATA 'WallpaperControl.BuildTools/RemoteSigning'
    New-Item -ItemType Directory -Force $signing | Out-Null
    $keystore = Join-Path $signing 'wallpapercontrol-remote.p12'
    $passwordFile = Join-Path $signing 'password.dpapi'
    $previousPassword = $env:WC_REMOTE_SIGNING_PASSWORD
    $previousRoot = $env:DOTNET_ROOT
    try {
        if ((Test-Path -LiteralPath $keystore) -ne (Test-Path -LiteralPath $passwordFile)) { throw 'Incomplete signing identity: recover its matching key/password; never replace the key silently.' }
        if (Test-Path -LiteralPath $passwordFile) {
            $password = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($passwordFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
        } else { $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
        $env:WC_REMOTE_SIGNING_PASSWORD = $password
        if (!(Test-Path -LiteralPath $keystore)) {
            & (Join-Path $JavaSdk 'bin/keytool.exe') -genkeypair -keystore $keystore -storetype PKCS12 -alias wallpapercontrol-remote -storepass:env WC_REMOTE_SIGNING_PASSWORD -keypass:env WC_REMOTE_SIGNING_PASSWORD -dname 'CN=WallpaperControl Remote, O=WallpaperControl, C=DE' -keyalg RSA -keysize 3072 -validity 10950 -noprompt *> "$logs/signing-key-generation.log"
            if ($LASTEXITCODE -ne 0) { throw 'Release signing key creation failed' }
            [IO.File]::WriteAllBytes($passwordFile,[Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($password),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
        }
        $env:DOTNET_ROOT = Split-Path -Parent $Dotnet11
        Push-Location WallpaperControl.Remote.Android
        try {
            & $Dotnet11 publish WallpaperControl.Remote.Android.csproj -c Release "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keystore" -p:AndroidSigningKeyAlias=wallpapercontrol-remote -p:AndroidSigningStorePass=env:WC_REMOTE_SIGNING_PASSWORD -p:AndroidSigningKeyPass=env:WC_REMOTE_SIGNING_PASSWORD *> "$logs/android-publish.log"
            $exitCode = $LASTEXITCODE
            $text = [IO.File]::ReadAllText("$logs/android-publish.log")
            if ($text.Contains($password)) { [IO.File]::WriteAllText("$logs/android-publish.log",$text.Replace($password,'[REDACTED]')); throw 'Signing task exposed a password in its log; log redacted and build rejected.' }
            if ($exitCode -ne 0) { throw 'Android signed publish failed' }
            Copy-Item -LiteralPath 'bin/Release/net11.0-android/android-arm64/publish/de.wallpapercontrol.remote-Signed.apk' -Destination "$output/WallpaperControl.Remote.apk"
        } finally { Pop-Location }
    } finally { $env:WC_REMOTE_SIGNING_PASSWORD = $previousPassword; $env:DOTNET_ROOT = $previousRoot; $password = $null }
    $previousJava = $env:JAVA_HOME
    try {
        $env:JAVA_HOME = $JavaSdk
        & (Join-Path $AndroidSdk 'build-tools/37.0.0/apksigner.bat') verify --verbose --print-certs "$output/WallpaperControl.Remote.apk" > "$logs/apk-verification.log"
        if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
    } finally { $env:JAVA_HOME = $previousJava }
    Copy-Item -LiteralPath docs/REMOTE_PRODUCTION_GUIDE.md -Destination "$output/README.md" -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Name -ne 'manifest.json' -and $_.FullName -notlike "$logs*" } | ForEach-Object {
        [pscustomobject]@{File=$_.FullName.Substring($output.Length+1);Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    } | ConvertTo-Json | Set-Content "$output/manifest.json" -Encoding utf8
    Write-Output "Production build: $output"
} finally { Pop-Location }
