from pathlib import Path
import json,hashlib,tarfile,shutil,datetime,subprocess,sys
base=Path(__file__).parent
old=base.parent/'libmpv-lgpl'
lock=json.loads((base.parent.parent/'outputs/WallpaperControl-LibMpv-LGPL/sources-lock.json').read_text())
identity='0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6'
name=sys.argv[1]
root=base/name
if root.exists():raise SystemExit('Refusing reuse of an existing build root')
root.mkdir();(root/'sources').mkdir();(root/'logs').mkdir();(root/'prefix/include').mkdir(parents=True)
evidence={'physicalRoot':str(root.resolve()),'startedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'logicalRoot':str(root.resolve()),'sources':[], 'customIdentity':identity, 'toolsAreVerifiedExistingCompilerInputsNotCandidateBuildProducts':True}
for entry in lock:
 archive=old/'sources'/Path(entry['url']).name
 # Archive names include component, unlike URL basename.
 candidates=list((old/'sources').glob(entry['component']+'-'+entry['revision']+'.tar.gz'))
 if len(candidates)!=1:raise RuntimeError(str(entry))
 archive=candidates[0]
 digest=hashlib.sha256(archive.read_bytes()).hexdigest().upper()
 if digest!=entry['sha256']:raise RuntimeError('source hash mismatch '+str(archive))
 target=root/'sources'/entry['component'];target.mkdir()
 with tarfile.open(archive) as tf:
  for member in tf.getmembers():
   parts=Path(member.name).parts
   if len(parts)<2:continue
   member.name=str(Path(*parts[1:]))
   tf.extract(member,target,filter='data')
 evidence['sources'].append(dict(entry,archive=str(archive),verifiedSha256=digest))
(root/'sources/mpv/MPV_VERSION').write_text(identity+'\n')
shutil.copytree(root/'sources/vulkan-headers/include',root/'prefix/include',dirs_exist_ok=True)
for tool in ['llvm-mingw-20260922-ucrt-x86_64','python-tools','msys64']:
 # Reuse immutable tool input only. Each candidate library is compiled from scratch.
 subprocess.run(['powershell','-NoProfile','-Command',f"New-Item -ItemType Junction -Path '{root/tool}' -Target '{old/tool}' | Out-Null"],check=True)
recipe=(old/'build.py').read_text()
(root/'build.py').write_text(recipe)
(root/'build-inputs.json').write_text(json.dumps(evidence,indent=2))
print('Prepared clean build '+name,flush=True)
