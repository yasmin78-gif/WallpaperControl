# Third-party notices — isolated candidate

This candidate uses mpv, FFmpeg, libplacebo, libass, FreeType, FriBidi, HarfBuzz,
shaderc, glslang, SPIRV-Tools, SPIRV-Headers, SPIRV-Cross, LLVM runtime libraries,
and MinGW-w64 runtime/header code. See SBOM.md / SBOM.json for exact revisions,
relationships and reasons for inclusion. Actual upstream license texts are in
LICENSES/. Full copyright notices and per-file licenses remain in the pinned
source archives supplied in corresponding-source.zip.

Technical licensing interpretation for this configuration: LGPL-3.0-or-later
for the combined libmpv library, retaining all additional notices and permissions.
FFmpeg is configured with GPL/nonfree disabled and version3 enabled. mpv,
libplacebo and FriBidi permit later LGPL versions. The FreeType License (FTL)
is selected, not its alternative GPL license. The Apache shader libraries and
the FTL are considered together under version 3; an LGPL-2.1-only claim is not made.
This is an engineering inventory and interpretation, not a legal opinion or a
statement that an installer/release has already satisfied every obligation.

## Credits and license choices

- mpv-player / MPlayer / mplayer2 contributors: LGPL-2.1-or-later for the
  compiled non-GPL-only program files; version 3 selected for this combination.
- FFmpeg developers: LGPL-3.0-or-later plus the retained per-file permissive
  notices. This software uses libraries from the FFmpeg project.
- libplacebo contributors: LGPL-2.1-or-later.
- FriBidi contributors: LGPL-2.1-or-later.
- libass contributors: ISC; actual COPYRIGHT/COPYING and per-file notices apply.
- HarfBuzz contributors: Old MIT and applicable individual source notices.
- Portions of this software are copyright © 1996–2025 The FreeType Project
  (www.freetype.org). All rights reserved. The FTL and contributed MIT/X11
  notices are retained; source package LICENSE.TXT documents the license choice.
- Google shaderc / SPIRV-Tools contributors and SPIRV-Cross contributors:
  Apache-2.0; see actual upstream files for copyright notices and conditions.
- glslang contributors: aggregate BSD/MIT/Apache notices in LICENSE.txt and
  LICENSES/. This aggregate covers multiple upstream portions; no GPL-only
  runtime component was identified in the configured build.
- Khronos SPIRV-Headers: MIT-family per-file notices in its aggregate LICENSE.
- Khronos Vulkan-Headers: MIT/Apache-2.0 per-file choices; headers used for
  disabled Vulkan stub compilation, no Vulkan loader binary is shipped.
- LLVM Project contributors: Apache-2.0 WITH LLVM-exception; libc++, ABI and
  unwind runtime portions supplied by the pinned LLVM-MinGW package.
- MinGW-w64 contributors and identified third parties: the provider's complete
  permissive CRT/header notices are in LICENSES/MinGW-w64.

## Source, modification, relinking and future distribution

The supplied source archives correspond to the components built here; the build
recipe and source hashes identify their contents. They contain the original
GPL-licensed source files/documentation too, but those source-package licenses
do not imply those excluded files were incorporated in the candidate DLL.

For an actual distribution, publish the precise corresponding source, patches
(only the documented MPV_VERSION metadata change; no playback-source patch), configuration and build scripts alongside the binary,
retain all notices and LGPL/GPL texts, and implement the required user-facing
credits. A mere upstream download link or gpl=false flag is insufficient evidence
of compliance. Archive retention and the chosen GPLv3 section 6 distribution
method must be maintained for the actual release.

Static linkage of FFmpeg/libplacebo/FriBidi inside libmpv does not remove their
source/recombination requirements. The complete rebuildable library sources and
recipe are the proposed way to supply its corresponding source. If combined with
proprietary application code, LGPL section 4 must additionally be satisfied:
for a static/relink route this may require application object/relink material;
for a suitable shared-library route an interface-compatible modified library
must actually work. Do not prohibit modification or reverse engineering needed
to debug modifications to the LGPL-covered portions.

WallpaperControl is GPLv3. Its full corresponding application source/build
instructions can support the LGPL 4d0 recombination route, including rebuilding
the validation metadata for the user's modified runtime. The present strict hash
validation rejects a modified DLL, so simply calling this a replaceable DLL does
not establish the LGPL 4d1 shared-library route. Document and verify how a user can
rebuild/recombine/run a modified application and library; provide installation
information where GPLv3 section 6 / LGPL 4e requires it. Production validation is
unchanged in this investigation.

LLVM/MinGW runtime binaries are obtained from a precisely hashed toolchain
package with its provider license files. Their internal source commit provenance
has not been independently attested; do not describe this as an independently
reproduced LLVM toolchain. Review the complete release bundle before adoption.

Professional legal review should assess the final source/distribution method,
the modification/installation workflow, any EULA restrictions, and H.264/AAC
patent issues applicable to the distribution's jurisdictions. Removing x264
changes copyright dependencies; it does not settle codec patents.

Primary sources: [mpv Copyright at the pinned revision](https://github.com/mpv-player/mpv/blob/3186d369f9f090cd1363be0ac46a037824b702c6/Copyright),
[FFmpeg licensing guidance](https://ffmpeg.org/legal.html),
[FFmpeg LGPLv3 text](https://github.com/FFmpeg/FFmpeg/blob/5a54fcf75e0245111075b1c31593ba1919c25306/COPYING.LGPLv3),
[FreeType license choice](https://github.com/freetype/freetype/blob/526ec5c47b9ebccc4754c85ac0c0cdf7c85a5e9b/LICENSE.TXT).
