from pathlib import Path
import subprocess,json,time
base=Path(__file__).parent.resolve();exe=base/'diagnostics/bin-final/WallpaperControl.RegressionTests.exe';dll=base.parent.parent/'outputs/WallpaperControl-LibMpv-FinalCandidate/candidate-runtime/libmpv-2.dll';fixtures=base.parent/'video-phase2-fixtures';extra=base.parent/'libmpv-desktop';logs=base/'test-logs';logs.mkdir(exist_ok=True)
original='F:/Lager/Bilder/bgvideos/Cathedral_Dragoness_3440x1440.mp4';other='F:/Lager/Bilder/bgvideos/Moonlit_Temptation_3440x1440.mp4'
tests=[('controller',['--mpv-controller-checks']),('desktop',['--libmpv-desktop-checks',dll,fixtures/'loop-colors.mp4']),('preparation',['--libmpv-preparation-checks',dll,fixtures]),('fill',['--libmpv-fill-checks',dll,extra]),('audio-handoff',['--libmpv-audio-handoff-checks',dll,extra]),('candidate-extra',['--candidate-extra',dll,original]),('loop10',['--libmpv-desktop-loop',dll,original,'10']),('switch100',['--libmpv-switch',dll,original,other,'100','idle'])]
results=[]
for name,args in tests:
 print('START '+name,flush=True);start=time.monotonic()
 with (logs/(name+'.log')).open('w',encoding='utf8') as f:p=subprocess.run([str(exe),*map(str,args)],stdout=f,stderr=subprocess.STDOUT)
 content=(logs/(name+'.log')).read_text(errors='replace');result={'name':name,'arguments':list(map(str,args)),'exitCode':p.returncode,'seconds':round(time.monotonic()-start,3),'passCount':sum(x.startswith('PASS') for x in content.splitlines()),'failed':'FAIL' in content};results.append(result);(base/'test-results.json').write_text(json.dumps(results,indent=2));print('END '+json.dumps(result),flush=True)
 if p.returncode or result['failed']:raise SystemExit(1)
