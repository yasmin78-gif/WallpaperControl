[CmdletBinding()]
param(
    [string]$Adb = 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe',
    [int]$Cycles = 3,
    [string]$ExpectedVersion = '1.1.7',
    [int]$MinimumWidgets = 1
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/remote-phase07-device'
New-Item -ItemType Directory -Force $output | Out-Null
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $script:count++; Write-Output "PASS $Name"
}
function Read-Ui([string]$Name) {
    & $Adb shell uiautomator dump /sdcard/wc-widget-test.xml | Out-Null
    & $Adb pull /sdcard/wc-widget-test.xml "$output/$Name.xml" 2>&1 | Out-Null
    [xml](Get-Content -LiteralPath "$output/$Name.xml" -Raw)
}
$package = & $Adb shell dumpsys package de.wallpapercontrol.remote
Check ([bool]($package -match "versionName=$([regex]::Escape($ExpectedVersion))")) 'device has the expected signed update installed'
for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    & $Adb shell input keyevent KEYCODE_HOME
    Start-Sleep -Seconds 2
    & $Adb shell am kill de.wallpapercontrol.remote
    Start-Sleep -Milliseconds 500
    $oldPid = (& $Adb shell pidof de.wallpapercontrol.remote) -join ' '
    Check ([string]::IsNullOrWhiteSpace($oldPid)) "cycle $cycle starts with no app process (not force-stop)"
    $ui = Read-Ui "cold-$cycle-before"
    $root = @($ui.SelectNodes('//node') | Where-Object { $_.GetAttribute('resource-id') -eq 'de.wallpapercontrol.remote:id/visitor_root' })[0]
    Check ($null -ne $root) "cycle $cycle has a real placed widget on launcher"
    $bounds = [regex]::Matches($root.GetAttribute('bounds'), '\d+') | ForEach-Object { [int]$_.Value }
    & $Adb shell input tap ([int](($bounds[0]+$bounds[2])/2)) ([int](($bounds[1]+$bounds[3])/2))
    Start-Sleep -Seconds 4
    $ui = Read-Ui "cold-$cycle-after"
    $status = @($ui.SelectNodes('//node') | Where-Object { $_.GetAttribute('resource-id') -eq 'de.wallpapercontrol.remote:id/visitor_status' })
    Check ($status.Count -ge $MinimumWidgets -and @($status | Where-Object { $_.GetAttribute('text') -ne 'Zuletzt aktiv' }).Count -eq 0) "cycle $cycle confirms every placed widget directly on launcher without opening app"
    $newPid = ((& $Adb shell pidof de.wallpapercontrol.remote) -join ' ').Trim()
    Check (![string]::IsNullOrWhiteSpace($newPid)) "cycle $cycle process survives asynchronous discovery shutdown"
    foreach ($appProcess in ($newPid -split ' ')) {
        $log = & $Adb logcat "--pid=$appProcess" -b main -b system -d
        $log | Set-Content "$output/cycle-$cycle-pid-$appProcess.log" -Encoding utf8
        Check (!(($log -join "`n") -match 'Stack overflow|Fatal signal|FATAL EXCEPTION')) "cycle $cycle has no stack overflow or fatal signal"
    }
}
Write-Output "All $count actual Android device checks passed. Activation only; no visitorOff was sent."
