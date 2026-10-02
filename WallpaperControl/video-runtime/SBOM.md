# Candidate inventory

| Component | Version | Relationship | License | Revision |
|---|---|---|---|---|
| mpv | mpv v0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6 | core shared libmpv DLL, dynamically loaded by host | LGPL-2.1-or-later (chosen LGPL-3.0-or-later) | 3186d369f9f090cd1363be0ac46a037824b702c6 |
| ffmpeg | 8.0.git / avcodec 63.15.100 | statically incorporated in libmpv-2.dll | LGPL-3.0-or-later | 5a54fcf75e0245111075b1c31593ba1919c25306 |
| libplacebo | 7.360.1 | statically incorporated in libmpv-2.dll | LGPL-2.1-or-later | cee9b076f2c63104ccfd497fa79c39a867293ec4 |
| libass | 0.17.5 | statically incorporated in libmpv-2.dll | ISC | f61db567e6593df3470e91594bcd4ad2d0473aff |
| freetype | 2.14.1 | statically incorporated in libmpv-2.dll | FTL AND MIT AND MIT-Open-Group | 526ec5c47b9ebccc4754c85ac0c0cdf7c85a5e9b |
| fribidi | 1.0.16 | statically incorporated in libmpv-2.dll | LGPL-2.1-or-later | 68162babff4f39c4e2dc164a5e825af93bda9983 |
| harfbuzz | 14.5.1 | statically incorporated in libmpv-2.dll | MIT-Old | f20f4c20bcb715eeb7974854b0689688975fdeee |
| shaderc | 2026.5.0 | statically incorporated in libmpv-2.dll | Apache-2.0 | ba3e587dbc13d423c713e964ac08e094731a034d |
| spirv-cross | 0.68.0 | dynamic DLL | Apache-2.0 | aa217aeb6c9f0ace7a0ab233b28807edf45eb165 |
| glslang | Pinned commit; see source headers | statically incorporated in libmpv-2.dll | BSD-3-Clause AND BSD-2-Clause AND MIT AND Apache-2.0 (see aggregate license) | e1b562a8bed273a02f30b59b66a5d499793cede5 |
| spirv-tools | Pinned commit; see source headers | statically incorporated in libmpv-2.dll | Apache-2.0 | ef96ed763b43b59b33b31b362f09a02b729fa1c9 |
| spirv-headers | Pinned commit; see source headers | build-time header | MIT | 04fd3caa1e8267e4d95c806cad901181728e1006 |
| vulkan-headers | Pinned commit; see source headers | build-time header | MIT OR Apache-2.0 | c46850864f4661461b0f6cb9922c058ffea4915e |
| LLVM libc++/libc++abi | 23.1.2, pinned llvm-mingw 20260922 package | dynamic libc++.dll | Apache-2.0 WITH LLVM-exception | None |
| LLVM libunwind | 23.1.2, same pinned package | dynamic libunwind.dll | Apache-2.0 WITH LLVM-exception | None |
| MinGW-w64 CRT/headers | 15.0.0 (installed header) | static CRT and compile-time headers | LicenseRef-MinGW-w64-runtime (permissive multiple notices) | None |

The toolchain-supplied runtime DLLs are pinned by archive and final file hashes. Their internal source revisions have not been independently attested. Windows/NVIDIA system DLLs are not shipped. This is a custom inventory, not a standards-certified SPDX/CycloneDX document. Source archives, full notices and the build recipe accompany the candidate.

Final package: `WallpaperControl.libmpv.minimal` revision 1, x64, API 2.5. Exact final DLL hashes and size: `candidate-runtime/runtime-package.json`. Build A is the selected artifact; Build B hashes remain separately documented.
