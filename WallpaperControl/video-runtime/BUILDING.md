# Isolated Windows x64 build

This recipe builds the candidate from pinned upstream source. It does not install
or select the DLL in WallpaperControl. No playback-source patch is used. `MPV_VERSION` is set through the supported project-version input to identify this custom build. The diagnostic profile difference is described separately.

## Inputs and layout

Use a new short local work directory, e.g. `C:\wc-mpv-build`, outside the product
repository. Copy all files from `build-recipe/`, `sources-lock.json` and
`build-manifest.json` into it. Unpack the `sources/` archive files from
`corresponding-source.zip` there. Run `fetch_locked_sources.py` with Python 3.12:
it verifies each archive hash and extracts the exact locked revision, downloading
only missing archives from the locked URLs. No moving branch is used.

Download the two `toolDownloads` entries from build-manifest.json and verify
their SHA-256 values: LLVM-MinGW 20260922 UCRT x86_64 ZIP and MSYS2 base 20260927
tar.xz. Extract them below the work root. Their directories must be named
`llvm-mingw-20260922-ucrt-x86_64` and `msys64`. This is a private portable tool
environment, not a global compiler/MSYS installation. Python must be callable
by its absolute executable path; build.py uses that same interpreter throughout.

Install the following pinned wheels into `python-tools` using that interpreter:

```powershell
& $python -m pip install --target python-tools meson==1.10.0 ninja==1.13.0 cmake==4.2.3 Jinja2==3.1.6 MarkupSafe==3.0.3
```

The manifest gives the exact wheel names and hashes used (CPython 3.12/x64 for
MarkupSafe). For an audited rebuild first download and verify those wheels,
then install with `--no-index --find-links` pointing to the verified wheel directory.
The investigation used Python 3.12.14, Meson 1.10.0, CMake 4.2.3,
Ninja 1.13.0.git.kitware.jobserver-pipe-1, clang/lld 23.1.2.

Start the private MSYS bash once to perform its initial keyring setup. Install
only these additional tool packages into that private root:

| Package file | Tool |
|---|---|
| make-4.4.1-3-x86_64.pkg.tar.zst | GNU Make 4.4.1 |
| diffutils-3.12-1-x86_64.pkg.tar.zst | configure helper |
| nasm-3.02-1-x86_64.pkg.tar.zst | x86 assembler |
| mingw-w64-ucrt-x86_64-pkgconf-1~3.0.7-1-any.pkg.tar.zst | native pkgconf 3.0.7 |

Hashes are in `toolPackages` in the manifest. MSYS package URLs use
`https://repo.msys2.org/msys/x86_64/FILE`; the pkgconf URL uses
`https://repo.msys2.org/mingw/ucrt64/FILE`. After verifying files, use private
`pacman -U --noconfirm` with their MSYS paths. A future `pacman -Sy` may retrieve
different versions and is not the pinned reproduction procedure. The entire
base package versions used here are listed in `diagnostics/msys-packages.txt`.

Create `logs` before running build.py. Copy Vulkan-Headers' `include/` contents
to `prefix/include/` before building libplacebo; these are declarations for its
disabled Vulkan stubs, not the Vulkan loader or a renderer requirement.
Copy glslang, SPIRV-Headers and SPIRV-Tools source directories into shaderc's
`third_party/` as performed automatically by build.py. Their hashes are exactly
the versions listed in shaderc's locked DEPS file. No runtime source patch was
applied. Python/Jinja/MarkupSafe generate source during the build and are not
shipped runtime dependencies.

## Build commands

```powershell
& $python fetch_locked_sources.py
New-Item -ItemType Directory -Force logs, prefix/include | Out-Null
Copy-Item sources/vulkan-headers/include/* prefix/include -Recurse -Force
& $python build.py fonts
& $python build.py shader
& $python build.py placebo
& $python build.py ffmpeg
& $python build.py mpv
```

Run each command to completion and check its exit code. `build.py` sets compiler,
PATH, pkg-config and PYTHONPATH only for its subprocesses. Source/configuration
revisions, original configuration logs, exact FFmpeg shell script and command
lists are retained in diagnostics/. Root-dependent arguments are generated
from the selected isolated root; eight compilation jobs were used.

The stage arguments in build.py are authoritative:

- FreeType: static, all auto features disabled, HarfBuzz and compressed/font
  image dependencies disabled; FTL selected for distribution.
- FriBidi: static; docs/bin/tests off.
- HarfBuzz: static, FreeType enabled, raster/vector/gpu/subset/tests/utilities off.
- libass: static, DirectWrite/GDI font providers, NASM enabled; no fontconfig,
  libunibreak, iconv dependency or test utilities.
- shaderc: static combined archive copied to libshaderc_combined.a; shaderc.pc
  references that archive. glslang and SPIRV-Tools are inside it. CMake also
  builds helper/shared artifacts; they are not included in the runtime bundle.
- SPIRV-Cross: shared C API DLL, HLSL/GLSL retained; MSL/reflect/CPP/CLI/tests off.
- libplacebo: static, shaderc enabled; Vulkan/OpenGL/D3D11 backends disabled,
  demos/Dolby Vision/optional external dependencies off. mpv's own D3D11 path
  remains active independently of libplacebo's disabled GPU backends.
- FFmpeg: static, disable-everything/autodetect/network/GPL/nonfree/programs/docs/
  avdevice; enable-version3; H.264/AAC decoders, MOV demuxer, AAC/H.264 parsers,
  file protocol; both h264_d3d11va and **h264_d3d11va2** hardware accelerators;
  required avfilter buffers/sinks/format/resampling/scaling and standard
  avutil/swscale/swresample. No encoder or external codec library.
- mpv: libmpv shared, cplayer=false, gpl=false, Lua/JS/plugins disabled,
  auto-features disabled, gl=false, D3D11/D3D11VA/WASAPI/native threads enabled,
  shaderc/SPIRV-Cross enabled, build-date=false, C++ runtime link `-lc++`.

`h264_d3d11va` alone enabled the older DXVA2-style hardware configuration but
did not provide the AV_PIX_FMT_D3D11 configuration selected by mpv. The final
candidate enables both paths. This correction is recorded in the recipe and audit.

## Runtime packaging

Copy only these four DLLs into a new runtime directory:

1. `prefix/bin/libmpv-2.dll`
2. `prefix/bin/libspirv-cross-c-shared.dll`
3. `llvm-mingw-20260922-ucrt-x86_64/bin/libc++.dll`
4. `llvm-mingw-20260922-ucrt-x86_64/bin/libunwind.dll`

Use the pinned toolchain's `llvm-strip --strip-unneeded` on the copies.
Do not ship glslc.exe, libshaderc_shared.dll, libSPIRV-Tools-shared.dll, mpv.exe,
Python, MSYS2 or compiler tools. The actual PE dependency closure is in
PE-INVENTORY.json; Windows/UCRT/D3DCompiler/driver libraries are system
dependencies, not files copied from another machine. Use pe.py to audit a rebuild
and ensure no new non-system imports appear.

## Isolated tests and product compatibility

The final diagnostics use the explicit `wc-minimal-r1-script-free` profile tied to the exact four-DLL package. Every remaining native option error is fatal. The original proven runtime profile remains unchanged. No default backend or committed pin changes.

The corresponding-source package includes the application source at the recorded commit and a separate compatibility/diagnostic patch. Apply it only to a separate checkout/copy. Build Debug and Release with `dotnet build -p:RunAnalyzers=true`; the diagnostics assembly remains `WallpaperControl.RegressionTests` for the existing internal-access contract. Test arguments and paths are retained in `TEST-RESULTS.json`; adjust external fixture paths when recreating the test environment. Fixtures include the original two user videos, colored/black videos, aspect-ratio squares and two tones. These external user media are not redistributed.

All 78 native checks, 32 controller checks, 10 original-video loops and 100 prepared A/B switches are run sequentially after compilation completes. Do not overlap compiler work, other audio sources, or screen-covering windows with rendering/audio measurements. Package validation occurs before loading; the native API and custom runtime identity are checked after initialization. Manual launch instructions for the isolated real application are supplied in MANUELLER-TEST.md after its verification.

## Final custom identity and the two clean builds

After verifying/extracting the locked sources, run `set_identity.py` in the build root before configuration. It changes only upstream `MPV_VERSION` to:

`0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6`

Meson reads this as its project version; upstream `common/meson.build` uses its supported `vcs_tag` fallback when the archive has no Git metadata. Runtime `mpv-version` adds `mpv v`. This is a WallpaperControl custom identity, not an official release. It does not alter playback source. The same Python script is supplied in the corresponding-source package.

Two fresh source extractions, static-library builds and independent prefixes were used: `work/libmpv-final/clean-a` and `clean-b`. Only immutable compiler/tool inputs were shared, not candidate object files, archives, prefixes or DLL outputs. The LLVM runtime DLLs are supplied prebuilt by the locked toolchain, not rebuilt by us. Commands, timestamps, absolute paths, source verification and result hashes are retained for each build.

Three DLLs are byte-identical. libmpv differs at 133 bytes: 125 bytes in embedded ASCII/UTF-16 build/source/install/configuration paths and 8 bytes of the LLD-derived content build ID. Every executable/data/relocation section is identical except `.rdata` and `.buildid`. No unexplained difference remains. Original binaries were neither patched nor normalized. The analytical path comparison never writes a normalized binary. See `REPRODUCIBILITY.json` for offsets and section hashes.

Thus this recipe is documented and source-locked, but not bit-reproducible across arbitrary installation/build paths. Path-independent output could be improved with compiler file-prefix maps plus a fixed installation-prefix/DESTDIR setup and controlled configuration strings; no playback-source modification is required. A SUBST approach was attempted and abandoned because a HarfBuzz Python generator mixed physical and logical drive paths. It is not part of the supported final recipe. For byte-identity reconstruction, the recorded absolute path and complete environment are relevant. Only the tested Build A package is accepted by its exact manifest pin; Build B is not accepted by wildcard identity.


### Recreating the two test stages

For the original 78-check candidate matrix, extract `WallpaperControl-source-base.zip` into a separate source checkout and apply `compatibility-profile.patch` (only the explicit option-profile design, original runtime pin still rejects the candidate). Build the `diagnostic-source` project with its application references adjusted to that checkout. The copied Player performs exact package validation and then uses the declared minimal options directly. Its extra check deliberately verifies original production-pin rejection. This is the phase-5 source state used for the recorded measurements.

For the actual compatibility application and added product/UI tests, use another clean base checkout and apply `compatibility.patch` instead. This full isolated patch accepts exactly Build A's complete package and its profile while retaining the proven runtime and MFPlay default. Do not apply the two patches cumulatively; the full patch already contains the profile change. The native candidate matrix's original-pin-rejection extra check is intended for the first stage, not the deliberately accepting second stage. Controller/profile/package and product checks in the full patch cover the second stage.
