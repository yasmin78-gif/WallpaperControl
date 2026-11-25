$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $entries = @{}
    foreach ($project in @('Host','Android','Checks','Client')) {
        $assets = Get-Content "$project/obj/project.assets.json" -Raw | ConvertFrom-Json
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -ne 'package') { continue }
            $identity = $library.Name.Split('/')
            $nuspec = Join-Path $env:USERPROFILE ('.nuget/packages/' + $identity[0].ToLowerInvariant() + '/' + $identity[1] + '/' + $identity[0].ToLowerInvariant() + '.nuspec')
            [xml]$xml = Get-Content -LiteralPath $nuspec -Raw
            $meta = $xml.package.metadata
            $entries[$library.Name] = [pscustomobject]@{ Package=$identity[0]; Version=$identity[1]; License=[string]$meta.license.'#text'; LicenseType=[string]$meta.license.type; LicenseUrl=[string]$meta.licenseUrl; ProjectUrl=[string]$meta.projectUrl }
        }
    }
    New-Item -ItemType Directory -Force evidence | Out-Null
    $entries.Values | Sort-Object Package,Version | ConvertTo-Json -Depth 5 | Set-Content evidence/dependencies.json -Encoding utf8
} finally { Pop-Location }
