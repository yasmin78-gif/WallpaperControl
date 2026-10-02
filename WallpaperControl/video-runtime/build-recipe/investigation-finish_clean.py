from pathlib import Path
import subprocess,hashlib,json,shutil,datetime,sys
base=Path(__file__).parent.resolve();old=base.parent/'libmpv-lgpl';sys.path.insert(0,str(old));from pe import inspect
name=sys.argv[1];root=base/name;runtime=root/'runtime';runtime.mkdir(exist_ok=True)
for file,src in [('libmpv-2.dll',root/'prefix/bin'),('libspirv-cross-c-shared.dll',root/'prefix/bin'),('libc++.dll',old/'llvm-mingw-20260922-ucrt-x86_64/bin'),('libunwind.dll',old/'llvm-mingw-20260922-ucrt-x86_64/bin')]:
 shutil.copy2(src/file,runtime/file)
 subprocess.run([str(old/'llvm-mingw-20260922-ucrt-x86_64/bin/llvm-strip.exe'),'--strip-unneeded',str(runtime/file)],check=True)
probe=subprocess.run([sys.executable,str(old/'runtime_probe.py'),str(runtime/'libmpv-2.dll')],capture_output=True,text=True,check=True)
(root/'runtime-probe.json').write_text(probe.stdout)
records=[inspect(p) for p in sorted(runtime.glob('*.dll'))]
(root/'dll-inventory.json').write_text(json.dumps(records,indent=2))
inputs=json.loads((root/'build-inputs.json').read_text());inputs['finishedUtc']=datetime.datetime.now(datetime.timezone.utc).isoformat();inputs['artifacts']=records
(root/'build-inputs.json').write_text(json.dumps(inputs,indent=2))
print(name,json.dumps([{k:v for k,v in x.items() if k in ['file','sha256','size']} for x in records],indent=2))
