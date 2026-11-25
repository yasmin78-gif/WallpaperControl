$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$catalog = @{}
$emittedTexts = [Collections.Generic.HashSet[string]]::new()
$notices = [Text.StringBuilder]::new()
[void]$notices.AppendLine('WallpaperControl Remote – third-party package notices')
[void]$notices.AppendLine('WallpaperControl source code is licensed under GPL-3.0-only. The following dependencies retain their respective licenses.')
foreach ($project in @('WallpaperControl.Remote.Android','WallpaperControl.Remote.Server','WallpaperControl.Remote.Client','WallpaperControl')) {
    $assets = Get-Content -LiteralPath (Join-Path $repo "$project/obj/project.assets.json") -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $catalog.ContainsKey($library.Name)) { continue }
        $parts = $library.Name.Split('/')
        $folder = Join-Path $env:USERPROFILE ('.nuget/packages/' + $parts[0].ToLowerInvariant() + '/' + $parts[1])
        [xml]$nuspec = Get-Content -LiteralPath (Join-Path $folder ($parts[0].ToLowerInvariant()+'.nuspec')) -Raw
        $metadata = $nuspec.package.metadata
        $license = [string]$metadata.license.'#text'
        $catalog[$library.Name] = [pscustomobject]@{Package=$parts[0];Version=$parts[1];License=$license;LicenseUrl=[string]$metadata.licenseUrl;Authors=[string]$metadata.authors;Copyright=[string]$metadata.copyright}
        [void]$notices.AppendLine("`n============================================================")
        [void]$notices.AppendLine($library.Name)
        [void]$notices.AppendLine('License: ' + $license)
        [void]$notices.AppendLine('Authors: ' + [string]$metadata.authors)
        [void]$notices.AppendLine([string]$metadata.copyright)
        [void]$notices.AppendLine([string]$metadata.licenseUrl)
        $files = @(Get-ChildItem -LiteralPath $folder -File | Where-Object Name -Match 'LICENSE|NOTICE')
        if ($metadata.license.type -eq 'file') { $specific = Join-Path $folder $license; if (Test-Path -LiteralPath $specific) { $files += Get-Item -LiteralPath $specific } }
        foreach ($file in ($files | Sort-Object FullName -Unique)) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
            if ($emittedTexts.Add($hash)) { [void]$notices.AppendLine("License text SHA-256: $hash"); [void]$notices.AppendLine($text) }
            else { [void]$notices.AppendLine("Identical license text already included above (SHA-256: $hash).") }
        }
    }
}
$raw = Join-Path $repo 'WallpaperControl.Remote.Android/Resources/Raw'
New-Item -ItemType Directory -Force $raw | Out-Null
[IO.File]::WriteAllText((Join-Path $raw 'third-party-notices.txt'), $notices.ToString(), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $raw 'license.txt')
$evidence = Join-Path $repo 'docs/remote-evidence'
New-Item -ItemType Directory -Force $evidence | Out-Null
$catalog.Values | Sort-Object Package,Version | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'dependencies.json') -Encoding utf8
Write-Output ("Inventoried {0} dependency versions." -f $catalog.Count)
