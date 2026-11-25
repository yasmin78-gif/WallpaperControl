[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$android = Join-Path $repo 'WallpaperControl.Remote.Android'
$count = 0
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $script:count++; Write-Output "PASS $Name"
}
[xml]$manifest = Get-Content -LiteralPath "$android/obj/Release/net11.0-android/android-arm64/android/AndroidManifest.xml" -Raw
$ns = 'http://schemas.android.com/apk/res/android'
foreach ($name in @('CompactVisitorWidget','SwitchVisitorWidget','VisitorWidgetActionReceiver')) {
    $receiver = @($manifest.manifest.application.receiver | Where-Object { $_.GetAttribute('name',$ns) -eq "de.wallpapercontrol.remote.$name" })
    Check ($receiver.Count -eq 1 -and $receiver[0].GetAttribute('exported',$ns) -eq 'false') "merged $name is private and registered exactly once"
}
$action = $manifest.manifest.application.receiver | Where-Object { $_.GetAttribute('name',$ns) -eq 'de.wallpapercontrol.remote.VisitorWidgetActionReceiver' }
Check ($null -eq $action.'intent-filter') 'control receiver has no externally matched intent filter'
$dialog = $manifest.manifest.application.activity | Where-Object { $_.GetAttribute('name',$ns) -eq 'de.wallpapercontrol.remote.VisitorOffActivity' }
Check ($dialog.GetAttribute('exported',$ns) -eq 'false' -and $dialog.GetAttribute('excludeFromRecents',$ns) -eq 'true') 'deactivation dialog is private and excluded from recents'
Check ($dialog.GetAttribute('taskAffinity',$ns) -eq 'de.wallpapercontrol.remote.widgetconfirmation') 'confirmation dialog uses a separate task instead of bringing the full app forward'
$allowed = @('android.permission.INTERNET','android.permission.ACCESS_NETWORK_STATE','android.permission.ACCESS_LOCAL_NETWORK','android.permission.CAMERA','de.wallpapercontrol.remote.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION')
$unexpected = @($manifest.manifest.'uses-permission' | Where-Object { $_.GetAttribute('name',$ns) -notin $allowed })
Check ($unexpected.Count -eq 0) 'widgets introduce no foreground-service, alarm, boot, location, wake-lock or other permission'
Check ($manifest.manifest.application.GetAttribute('usesCleartextTraffic',$ns) -eq 'false' -and $manifest.manifest.application.GetAttribute('allowBackup',$ns) -eq 'false') 'cleartext and application backup remain disabled'
Check ($manifest.manifest.application.GetAttribute('icon',$ns) -eq '@mipmap/appicon' -and $manifest.manifest.application.GetAttribute('roundIcon',$ns) -eq '@mipmap/appicon_round') 'application explicitly references both actual launcher icons'
foreach ($variant in @(@('compact',1),@('switch',2))) {
    [xml]$info = Get-Content -LiteralPath "$android/Platforms/Android/Resources/xml/visitor_$($variant[0])_info.xml" -Raw
    Check ($info.'appwidget-provider'.GetAttribute('targetCellWidth',$ns) -eq [string]$variant[1] -and $info.'appwidget-provider'.GetAttribute('targetCellHeight',$ns) -eq '1') "$($variant[0]) exposes the requested launcher grid dimensions"
    Check ([int]$info.'appwidget-provider'.GetAttribute('updatePeriodMillis',$ns) -ge 1800000) "$($variant[0]) background refresh is at least thirty minutes"
    [xml]$layout = Get-Content -LiteralPath "$android/Platforms/Android/Resources/layout/visitor_$($variant[0]).xml" -Raw
    $types = @($layout.SelectNodes('//*') | ForEach-Object Name | Sort-Object -Unique)
    Check (@($types | Where-Object { $_ -notin @('LinearLayout','ImageView','TextView','ProgressBar') }).Count -eq 0) "$($variant[0]) layout uses only supported native RemoteViews types"
}
Write-Output "All $count native widget packaging checks passed. No Android device execution is implied."
