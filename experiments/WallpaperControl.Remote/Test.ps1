$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force out | Out-Null
    & dotnet run --project Checks/Checks.csproj -c Release *> out/checks.log
    $result = $LASTEXITCODE
    Get-Content out/checks.log
    if ($result -ne 0) { throw "Checks failed: $result" }
    Copy-Item Checks/bin/Release/net10.0-windows/measurements.json out/measurements.json
} finally { Pop-Location }
