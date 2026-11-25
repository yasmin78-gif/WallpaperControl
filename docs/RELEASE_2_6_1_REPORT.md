# WallpaperControl 2.6.1 – Release preparation

- Windows version: **2.6.1**, file version **2.6.1.0**.
- Android: accepted signed **1.1.7**, versionCode **10**. The release APK is byte-for-byte identical to the accepted device build; its original signing identity and application ID remain intact.
- Installer built with Inno Setup **6.7.3** from a fresh self-contained Windows x64 publish directory. The pinned video runtime manifest and native payload hashes were verified by the existing build script.
- The compiler download was verified against the official GitHub release SHA-256 and its valid Authenticode signature before installation in the private build-tools directory.
- Isolated installer tests passed: installation, payload hashes, executable version, start-menu registration, disabled initial autostart, upgrade migration of enabled autostart, uninstall cleanup and preservation of an autostart command owned by another copy. The real WallpaperControl installation was not replaced or started by these tests.
- README files updated in **English, German, French, Spanish and Japanese**, including installer/APK links, versions, pairing and Android background limitations. Public release notes describe the desktop and mobile components together.

Release assets: Windows installer, signed APK, one SHA-256 file for each binary, and combined `SHA256SUMS.txt`. Sizes and digests are recorded in [release-2.6.1-assets.json](remote-evidence/phase07/release-2.6.1-assets.json). The draft upload is verified against GitHub's asset SHA-256 digests before publication; final verification checks the public release, latest marker, notes, tag target and asset set.

Target: the approved `codex/remote-production` development history plus these release documentation changes. No merge into another branch is required or performed. Release tag: **v2.6.1**. The user explicitly authorized creation of the installer, multilingual README updates and publication of the release including the APK.

Installer validation: [test log](remote-evidence/phase07/installer-2.6.1-tests.txt). Prior development validation includes 102 Remote checks, 15 Android packaging checks, the previously executed 6302 desktop regression checks, and the accepted real Pixel tests documented in [Phase 07](REMOTE_PHASE_07_ANDROID_VISITOR_WIDGET_REPORT.md).
