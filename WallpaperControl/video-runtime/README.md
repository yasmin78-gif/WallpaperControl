# Accepted Standard video runtime

This directory contains the exact final minimal candidate accepted for production integration. No DLL was rebuilt or substituted.

`package/` contains the four inseparable x64 DLLs and the original pinned `runtime-package.json`. Its relative `../SBOM.json` and `../THIRD_PARTY_NOTICES.md` references resolve in this layout. The complete directory is copied to build and publish outputs.

The original SBOM, notices, licenses, source lock, BUILDING instructions, build recipe, source archive and candidate patches are preserved byte-for-byte. Their historical candidate labels describe the build provenance; the subsequent production approval does not change their content. The archive describes the accepted runtime and candidate integration, not a newly generated source snapshot of the current application.

Normal startup finds `video-runtime/package/libmpv-2.dll` relative to the application directory. Standard is the default preference for missing or unknown settings. Compatibility uses the retained Windows MFPlay implementation.

Development overrides are explicit and logged. Precedence: `--video-backend=libmpv|mfplay` overrides the saved engine for that run; otherwise the saved engine is used. `--mpv-runtime=<absolute local path>` is accepted only alongside an explicit backend override and remains subject to the exact four-DLL package validation. It does not select arbitrary builds. No command-line arguments are required for normal operation, and overrides do not persist the engine preference.

The module is retained for the process lifetime, as before. Playback sessions are disposed asynchronously before another engine starts. Package/native initialization failures can return a visible Compatibility fallback without changing the saved Standard preference. Later media/decode errors use the normal controller error path and do not switch engines. To retry a restored package during a fallback, click Standard again; restart the application if the files cannot be changed because Windows still owns the loaded module.

Installer integration remains a separate acceptance step.
