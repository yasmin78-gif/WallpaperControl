param(
    [Parameter(Mandatory = $true)][string]$Ffmpeg,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$fixtureDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $fixtureDirectory | Out-Null
function Invoke-FixtureEncoder([string[]]$EncoderArguments) {
    & $Ffmpeg -hide_banner -loglevel error -y @EncoderArguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture encoder failed: $LASTEXITCODE" }
}
Invoke-FixtureEncoder @('-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=4',
    '-f','lavfi','-i','sine=frequency=880:sample_rate=48000:duration=4',
    '-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac','-shortest',(Join-Path $fixtureDirectory 'h264.mp4'))
Invoke-FixtureEncoder @('-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=1',
    '-c:v','mpeg4',(Join-Path $fixtureDirectory 'unsupported.mp4'))
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'corrupt.mp4') -Value 'Not an MP4.'
foreach ($scene in @(@{ Name = 'loop-colors.mp4'; First = 'blue' }, @{ Name = 'loop-black.mp4'; First = 'black' })) {
    Invoke-FixtureEncoder @('-f','lavfi','-i',"color=c=$($scene.First):s=320x180:r=30:d=1",
        '-f','lavfi','-i','color=c=red:s=320x180:r=30:d=1',
        '-f','lavfi','-i','sine=frequency=880:sample_rate=48000:duration=2',
        '-filter_complex','[0:v][1:v]concat=n=2:v=1:a=0[v]','-map','[v]','-map','2:a',
        '-c:v','libx264','-pix_fmt','yuv420p','-g','30','-c:a','aac','-shortest',(Join-Path $fixtureDirectory $scene.Name))
}
