from pathlib import Path
import subprocess,sys,os,json,shutil
root=Path(__file__).parent.resolve(); src=root/'sources';build=root/'build';prefix=root/'prefix'
build.mkdir(exist_ok=True);prefix.mkdir(exist_ok=True)
llvm=root/'llvm-mingw-20260922-ucrt-x86_64/bin';cmake=root/'python-tools/cmake/data/bin/cmake.exe';ninja=root/'python-tools/bin/ninja.exe';pkg=root/'msys64/ucrt64/bin/pkgconf.exe'
env=os.environ.copy();env['PATH']=os.pathsep.join(map(str,[llvm,ninja.parent,cmake.parent,pkg.parent,root/'msys64/usr/bin']))+os.pathsep+env['PATH']
env['PATH']=str(Path(sys.executable).parent)+os.pathsep+env['PATH']
env['PYTHONPATH']=str(root/'python-tools');env['PKG_CONFIG_PATH']=str(prefix/'lib/pkgconfig');env['PKG_CONFIG']=str(pkg);env['SOURCE_DATE_EPOCH']='1790817551'
env['CC']='x86_64-w64-mingw32-clang';env['CXX']='x86_64-w64-mingw32-clang++';env['AR']='llvm-ar';env['RANLIB']='llvm-ranlib';env['STRIP']='llvm-strip'
commands=[]
def run(name,args,cwd=None):
    args=list(map(str,args));commands.append(dict(name=name,args=args,cwd=str(cwd or root)))
    (root/('commands-'+(sys.argv[1] if len(sys.argv)>1 else 'all')+'.json')).write_text(json.dumps(commands,indent=2))
    print(name,flush=True)
    with (root/'logs'/f'{name}.log').open('w',encoding='utf-8') as log:
        p=subprocess.run(args,cwd=cwd or root,env=env,stdout=log,stderr=subprocess.STDOUT)
    if p.returncode:
        print((root/'logs'/f'{name}.log').read_text(errors='replace')[-7000:],flush=True);raise SystemExit(p.returncode)
def meson(name,opts):
    dest=build/name
    if not (dest/'build.ninja').exists():
        run(name+'-configure',[sys.executable,'-m','mesonbuild.mesonmain','setup',dest,src/name,'--prefix='+prefix.as_posix(),'--libdir=lib','--buildtype=release','--default-library=static','--auto-features=disabled','--wrap-mode=nofallback',*opts])
    run(name+'-build',[ninja,'-C',dest,'-j','8']);run(name+'-install',[ninja,'-C',dest,'install'])
def cm(name,opts,source=None):
    dest=build/name
    run(name+'-configure',[cmake,'-S',source or src/name,'-B',dest,'-G','Ninja','-DCMAKE_BUILD_TYPE=Release','-DCMAKE_C_COMPILER='+str(llvm/'x86_64-w64-mingw32-clang.exe'),'-DCMAKE_CXX_COMPILER='+str(llvm/'x86_64-w64-mingw32-clang++.exe'),'-DCMAKE_INSTALL_PREFIX='+prefix.as_posix(),'-DCMAKE_INSTALL_LIBDIR=lib','-DBUILD_SHARED_LIBS=OFF','-DCMAKE_POLICY_VERSION_MINIMUM=3.5',*opts])
    run(name+'-build',[cmake,'--build',dest,'--parallel','8']);run(name+'-install',[cmake,'--install',dest])
if len(sys.argv)<2 or sys.argv[1]=='fonts':
    meson('freetype',['-Dharfbuzz=disabled','-Dzlib=disabled'])
    meson('fribidi',['-Ddocs=false','-Dbin=false','-Dtests=false'])
    meson('harfbuzz',['-Dfreetype=enabled','-Draster=disabled','-Dvector=disabled','-Dgpu=disabled','-Dsubset=disabled','-Dtests=disabled','-Dutilities=disabled'])
    meson('libass',['-Ddirectwrite=enabled','-Dasm=enabled'])
if len(sys.argv)<2 or sys.argv[1]=='shader':
    shader=src/'shaderc'
    for name in ['glslang','spirv-tools','spirv-headers']:
        target=shader/'third_party'/name
        if not target.exists():shutil.copytree(src/name,target)
    cm('shaderc',['-DPython_EXECUTABLE='+sys.executable,'-DPython3_EXECUTABLE='+sys.executable,'-DSHADERC_SKIP_TESTS=ON','-DSHADERC_SKIP_EXAMPLES=ON','-DSHADERC_SKIP_INSTALL=OFF','-DSPIRV_SKIP_EXECUTABLES=ON','-DSPIRV_SKIP_TESTS=ON','-DENABLE_GLSLANG_BINARIES=OFF','-DENABLE_GLSLANG_JS=OFF'])
    # Combined static archive has all compiler dependencies. No separate compiler DLLs.
    shutil.copy2(build/'shaderc/libshaderc/libshaderc_combined.a',prefix/'lib/libshaderc_combined.a')
    pc=(prefix/'lib/pkgconfig/shaderc_combined.pc').read_text();(prefix/'lib/pkgconfig/shaderc.pc').write_text(pc)
    cm('spirv-cross',['-DSPIRV_CROSS_SHARED=ON','-DSPIRV_CROSS_CLI=OFF','-DSPIRV_CROSS_ENABLE_TESTS=OFF','-DSPIRV_CROSS_ENABLE_MSL=OFF','-DSPIRV_CROSS_ENABLE_REFLECT=OFF','-DSPIRV_CROSS_ENABLE_CPP=OFF'])
if len(sys.argv)<2 or sys.argv[1]=='placebo':
    meson('libplacebo',['-Ddemos=false','-Ddovi=disabled','-Dshaderc=enabled'])
if len(sys.argv)<2 or sys.argv[1]=='ffmpeg':
    dest=build/'ffmpeg';dest.mkdir(exist_ok=True)
    args=['--prefix='+prefix.as_posix(),'--target-os=mingw32','--arch=x86_64','--cc=x86_64-w64-mingw32-clang','--cxx=x86_64-w64-mingw32-clang++','--ar=llvm-ar','--ranlib=llvm-ranlib','--strip=llvm-strip','--pkg-config='+pkg.as_posix(),'--disable-everything','--disable-autodetect','--disable-gpl','--disable-nonfree','--enable-version3','--disable-shared','--enable-static','--disable-programs','--disable-doc','--disable-network','--disable-avdevice','--enable-decoder=h264,aac','--enable-demuxer=mov','--enable-parser=h264,aac','--enable-protocol=file','--enable-hwaccel=h264_d3d11va,h264_d3d11va2','--enable-d3d11va','--enable-filter=abuffer,abuffersink,buffer,buffersink,format,aformat,aresample,scale','--extra-cflags=-O2']
    # Pass arguments through an explicitly generated shell script, never shell-interpolate paths.
    import shlex
    script=dest/'configure-build.sh'
    lines=['set -eu','export PATH='+shlex.quote('/'+str(llvm)[0].lower()+str(llvm)[2:].replace('\\','/'))+':$PATH','cd '+shlex.quote('/'+str(dest)[0].lower()+str(dest)[2:].replace('\\','/')),shlex.quote('/'+str(src)[0].lower()+str(src)[2:].replace('\\','/')+'/ffmpeg/configure')+' '+' '.join(map(shlex.quote,args)),'make -j8','make install']
    script.write_text('\n'.join(lines)+'\n')
    run('ffmpeg-build',[root/'msys64/usr/bin/bash.exe',script.as_posix()],dest)
if len(sys.argv)<2 or sys.argv[1]=='mpv':
    meson('mpv',['--default-library=shared','-Dc_link_args=-lc++','-Dgpl=false','-Dcplayer=false','-Dlibmpv=true','-Dbuild-date=false','-Dlua=disabled','-Dwin32-threads=enabled','-Dd3d11=enabled','-Dd3d-hwaccel=enabled','-Dwasapi=enabled','-Dshaderc=enabled','-Dspirv-cross=enabled','-Dgl=disabled'])
