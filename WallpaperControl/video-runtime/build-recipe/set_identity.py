from pathlib import Path
p=Path(__file__).parent/'sources/mpv/MPV_VERSION'
p.write_text('0.41.0-wc-minimal-r1-x64-api2.5-g3186d369f9f090cd1363be0ac46a037824b702c6\n')
