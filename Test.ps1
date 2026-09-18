param([string]$Configuration = 'Release', [switch]$VerifyFailurePropagation)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'WallpaperControl.RegressionTests/WallpaperControl.RegressionTests.csproj'
$runArguments = @('run', '--project', $projectPath, '-c', $Configuration)
if ($VerifyFailurePropagation) { $runArguments += @('--', '--fail-test') }
$testOutput = & dotnet @runArguments 2>&1
$testExitCode = $LASTEXITCODE
$testOutput | ForEach-Object { Write-Host $_ }
if ($testExitCode -ne 0) { exit $testExitCode }
$summary = $testOutput | Select-String '^All ([1-9][0-9]*) checks passed\.'
if (-not $summary) {
    Write-Error 'Regression runner did not report a non-zero executed check count.'
    exit 1
}
exit 0
