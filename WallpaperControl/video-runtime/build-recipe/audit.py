from pathlib import Path
import json,re,fnmatch
root=Path(__file__).parent
commands=json.loads((root/'build/mpv/compile_commands.json').read_text())
files=[str(Path(x['file'])).replace('\\','/') for x in commands]
gpl=['audio/out/ao_jack.c','audio/out/ao_oss.c','stream/dvb*','stream/stream_cdda.c','stream/stream_dvb.*','stream/stream_dvdnav.c','video/out/vo_caca.c','video/out/vo_direct3d.c','video/out/vo_vaapi.c','video/out/vo_vdpau.c','video/out/vo_x11.c','video/out/vo_xv.c','video/out/x11_common.*','video/vdpau.c','video/vdpau_mixer.*']
hits=[f for f in files if any(fnmatch.fnmatch(f,'*'+pattern) for pattern in gpl)]
config=(root/'build/ffmpeg/config.h').read_text();components=(root/'build/ffmpeg/config_components.h').read_text()
enabled=re.findall(r'^#define (CONFIG_\w+) 1$',components,re.M)
result={'mpvCompilationUnits':len(files),'mpvListedGplOnlyCompilationUnits':hits,'ffmpegLicenseFlags':re.findall(r'^#define (CONFIG_(?:GPL|NONFREE|VERSION3|GPLV3)) ([01])$',config,re.M),'enabledDecoders':[x for x in enabled if x.endswith('_DECODER')],'enabledEncoders':[x for x in enabled if x.endswith('_ENCODER')],'enabledDemuxers':[x for x in enabled if x.endswith('_DEMUXER')],'enabledProtocols':[x for x in enabled if x.endswith('_PROTOCOL')],'enabledHardwareAcceleration':[x for x in enabled if x.endswith('_HWACCEL')],'enabledFilters':[x for x in enabled if x.endswith('_FILTER')],'patches':[]}
(root/'source-license-audit.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
