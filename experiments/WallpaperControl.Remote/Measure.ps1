[CmdletBinding()]
param([ValidateRange(5,600)][int]$Seconds = 30)
$ErrorActionPreference = 'Stop'
$rows = @()
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force out | Out-Null
    & dotnet build Host/Host.csproj -c Release *> out/measure-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    foreach ($mode in @('baseline','kestrel')) {
        $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSScriptRoot 'Host/bin/Release/net10.0-windows/Host.exe'))
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.WindowStyle = 'Hidden'; $info.RedirectStandardOutput = $true
        $info.ArgumentList.Add('--measure')
        if ($mode -eq 'baseline') { $info.ArgumentList.Add('--baseline') }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($info)
        try {
            $lineTask = $process.StandardOutput.ReadLineAsync()
            if (-not $lineTask.Wait(15000)) { throw 'Host readiness timeout' }
            $line = $lineTask.Result
            $ready = $watch.Elapsed.TotalMilliseconds
            if (!$line -or !($line | ConvertFrom-Json).ready) { throw 'Host not ready' }
            $process.Refresh(); $cpu = $process.TotalProcessorTime
            $sample = [Diagnostics.Stopwatch]::StartNew()
            Start-Sleep -Seconds $Seconds
            $process.Refresh()
            $rows += [pscustomobject]@{ Scenario=$mode; Seconds=$sample.Elapsed.TotalSeconds; ReadyMillisecondsIncludingOneSecondStabilization=$ready; PrivateBytes=$process.PrivateMemorySize64; WorkingSetBytes=$process.WorkingSet64; Threads=$process.Threads.Count; Handles=$process.HandleCount; CpuOneCorePercent=($process.TotalProcessorTime-$cpu).TotalSeconds/$sample.Elapsed.TotalSeconds*100 }
        } finally {
            # This process was created solely for this measurement. WM_CLOSE keeps normal async cleanup.
            $process.Refresh()
            if (!$process.HasExited) { [void]$process.CloseMainWindow(); if (!$process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() } }
            $process.Dispose()
        }
    }
    $rows | ConvertTo-Json | Set-Content out/process-measurements.json -Encoding utf8
    $rows | Format-Table -AutoSize
} finally { Pop-Location }
