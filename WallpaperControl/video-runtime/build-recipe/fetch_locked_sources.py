from pathlib import Path
import urllib.request,json,hashlib,tarfile,sys
root=Path(__file__).parent.resolve()
lock=json.loads((root/'sources-lock.json').read_text())
archives=root/'sources';archives.mkdir(exist_ok=True)
for record in lock:
    name=record['component'];revision=record['revision'];archive=archives/f'{name}-{revision}.tar.gz'
    if not archive.exists():
        request=urllib.request.Request(record['url'],headers={'User-Agent':'WallpaperControl-pinned-runtime-build'})
        data=urllib.request.urlopen(request,timeout=120).read()
        if hashlib.sha256(data).hexdigest().upper()!=record['sha256']:raise RuntimeError('Source hash mismatch: '+name)
        archive.write_bytes(data)
    if hashlib.sha256(archive.read_bytes()).hexdigest().upper()!=record['sha256']:raise RuntimeError('Cached source hash mismatch: '+name)
    dest=archives/name
    if not dest.exists():
        dest.mkdir()
        with tarfile.open(archive) as tar:
            for item in tar.getmembers():
                item.name='/'.join(item.name.split('/')[1:])
                if item.name:tar.extract(item,dest,filter='data')
    print(name,revision)
