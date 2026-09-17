# 🖼️ Wallpaper Control

🌐 **言語:** [English](README.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [日本語](README.ja.md)

**Wallpaper Control** は、デスクトップ壁紙のスライドショーを管理・表示するための軽量な Windows ユーティリティです。正確なスケジュール設定、アニメーション付きトランジション、統計、ネイティブのデスクトップウィジェット、さらに便利な操作機能を備えています。

Windows 標準の壁紙機能を、時刻に同期した独自のスライドショーエンジン、デスクトップ上に直接描画されるトランジション効果、オプションのデスクトップウィジェットで拡張します。Windows デスクトップに自然に統合され、アプリケーション終了時には Windows 標準の壁紙処理を復元します。

**現在のリリース: v1.8.3**

## ✨ 機能

- 🖼️ **壁紙スライドショーの制御**
  - 壁紙フォルダーを選択
  - スライドショーの間隔を変更
  - シャッフルの有効 / 無効を切り替え
  - 次の壁紙へ即座に切り替え
  - アクセスしやすいよう **次の壁紙** アクションを強調表示
  - 現在の壁紙専用セクション、クイック操作、フルパスのツールチップ
  - スライドショーの一時停止と再開
  - 追加モニターを含め、フルスクリーンアプリ実行中は自動的に一時停止
  - 2 秒の再開待機時間により、短い Alt-Tab 操作でバックグラウンド処理がすぐ再開されるのを防止
  - 手動で設定したスライドショーの一時停止状態は独立して維持
  - 現在の壁紙を固定

- 🎬 **壁紙トランジション効果**
  - Windows デスクトップ上に直接描画される滑らかなトランジション
  - Wipe: 左、右、上、下、ランダムから方向を選択可能
  - Slide: 左、右、上、下、ランダムから方向を選択可能
  - Fade
  - Zoom: In / Out バリエーション
  - Split
  - Curtain
  - ランダムモードでは壁紙変更ごとに異なる効果を選び、必要に応じて方向やズームモードもランダム化
  - トランジション時間を設定可能
  - デスクトップアイコンやデスクトップツールはトランジションレイヤーより上に表示

- 🕐 **ネイティブデスクトップウィジェット**
  - 5 種類のテーマを選べる時計ウィジェット
  - システム監視ウィジェット
  - オプションで 3 日間予報を表示できる天気ウィジェット
  - iCalendar / ICS 対応のカレンダーウィジェット
  - オプションの「次の壁紙」ボタン
  - 各ウィジェットをデスクトップ上の任意の位置に個別配置可能
  - ウィジェットごとに位置を個別にロック可能
  - ウィジェットの位置と設定を保存
  - 設定変更中にウィジェットをライブプレビュー
  - ウィジェットはデスクトップの一部として動作し、通常のアプリケーションウィンドウより前面には固定されません

- 🖥️ **Windows との統合**
  - Windows 標準の壁紙 API と統合しつつ、独自のスライドショータイミングとトランジションエンジンを提供
  - 時刻に同期した独自のスライドショータイミングで正確に壁紙を変更
  - 手動で壁紙を変更しても自動スライドショーのスケジュールはリセットされません
  - 複数の壁紙表示モードに対応
  - 外部からの壁紙変更を検出
  - 設定された既定のファイルマネージャーでフォルダーを開く
  - Windows 起動時の自動起動を任意で設定可能
  - Wallpaper Control 終了時に Windows 標準の壁紙処理を復元
  - Windows ユーザーごとに単一インスタンスで実行
  - Wallpaper Control を再度起動すると既存のウィンドウを前面に表示
  - コマンドライン引数 `--next` による外部からの壁紙切り替えに対応
  - `--next` コマンドは現在の Windows ユーザーで実行中のインスタンスへ安全に転送
  - ネイティブ時計ウィジェットの有効状態を外部アプリやスクリプトから取得可能

- 📊 **Statistics dashboard**
  - Persistent wallpaper statistics across application restarts
  - Tracks views and when each wallpaper was last displayed
  - Time-based statistics for Today, Yesterday, Last 7 Days and Last 30 Days
  - Top 10, Top 25 and complete statistics views
  - Dashboard metrics for most viewed, least viewed and average views
  - 表示分布の均等性 metric
  - Top 10 wallpaper chart
  - Average wallpaper recurrence time
  - Neglected wallpaper analysis
  - Detects wallpapers that have never been displayed
  - Search and sortable columns
  - Wallpaper thumbnails and hover previews
  - Set a wallpaper directly from the statistics window
  - Open wallpapers or their folders from the context menu
  - Remove individual entries or reset all statistics
  - Automatic backup and recovery if the main statistics file cannot be loaded
  - Damaged statistics files are preserved for possible recovery

- 🗑️ **Quick wallpaper rejection**
  - Move unwanted wallpapers to an `Aussortiert` folder with one click
  - Wallpaper rejection is temporarily disabled while a wallpaper transition is running
  - The next wallpaper is fully displayed before the rejected wallpaper is moved
  - Optional global rejection folder
  - Optional subfolders for individual wallpaper collections
  - Undo the last rejection

- 📜 **Wallpaper history**
  - Keeps track of recently displayed wallpapers during the current session
  - Open wallpapers directly in your default image viewer
  - Hover previews for quick identification

- ⌨️ **Global hotkeys**
  - Next wallpaper
  - Pause / Resume
  - Show current wallpaper in your file manager
  - Reject current wallpaper
  - Hotkeys can be customized or disabled
  - Detects duplicate hotkey assignments
  - Warns when Windows cannot register a selected hotkey
  - Hotkeys can be swapped between actions without conflicts from previous assignments
  - Unchanged hotkeys remain registered when other shortcuts are modified
  - Default Reject hotkey: `Ctrl+Alt+Shift+R`

- 🔔 **System tray support**
  - Wallpaper Control can continue running in the notification area
  - Double-click the tray icon to restore the window
  - Optional **Close to Tray** behavior when clicking the window's X button
  - Exit the application directly from the tray menu

- 🎨 **Interface & appearance**
  - Redesigned Settings interface
  - Main application redesigned to match the Settings interface
  - Consistent modern appearance across the application
  - System, Dark and Light theme selection
  - System theme automatically follows the Windows app theme
  - Adjustable window opacity
  - Remembers window position
  - Drag & drop support
  - Reorganized settings interface
  - Settings always open on the **General** tab
  - Refreshed main window with clearer grouping and improved visual hierarchy
  - Dark dropdowns and improved readability for disabled controls
  - Improved keyboard tab order and consistent spacing
  - Separate appearance reset
  - Localized interface

## 🎮 フルスクリーン時の一時停止

フルスクリーンアプリケーションが実行中の場合、Wallpaper Control はバックグラウンド処理を自動的に抑えることができます。

- 接続されているすべてのモニター上のフルスクリーンアプリを検出
- 壁紙の自動変更とトランジションアニメーションを一時停止
- デスクトップウィジェットの定期更新を停止
- 自動アップデート確認を延期
- フルスクリーンアプリがなくなってから 2 秒後に処理を再開
- 短い Alt-Tab 操作では一時停止中の処理がすぐに再開されません
- 手動で一時停止したスライドショーはフルスクリーン終了後も一時停止を維持
- フルスクリーン検出は既定で有効で、設定から無効化可能

## 🔄 アップデート確認

Wallpaper Control は、更新の主導権をユーザーに残したまま GitHub Releases で新しいバージョンを確認できます。

- 設定から手動でアップデートを確認可能
- Wallpaper Control 起動時の自動確認を任意で有効化
- アプリケーションを継続して実行している場合、自動確認は 24 時間ごとに実行
- 専用ダイアログにインストール済みバージョンと最新バージョンを表示
- 新しいバージョンがある場合はリリースページを直接開けます
- 自動アップデート確認は設定で無効化可能
- Wallpaper Control がアップデートを**自動でダウンロードまたはインストールすることはありません**

インストール済みバージョンが最新の場合、自動確認では通知を表示しません。手動確認では常に結果を表示します。

## 🕐 デスクトップウィジェット

Wallpaper Control には Windows デスクトップへ直接統合されるネイティブデスクトップウィジェットが含まれています。

各ウィジェットは個別に配置・固定でき、位置と設定はアプリケーションのセッションをまたいで保存されます。

設定画面でのウィジェット変更はすぐにプレビューされます。設定を保存すると確定し、キャンセルすると以前の状態と位置に戻ります。

### 時計

デスクトップ時計の機能:

- 時・分の表示
- 秒の表示を任意で有効化
- ローカライズされた日付形式
- サイズ調整
- 5 種類のビジュアルテーマ
- 自由な配置
- 任意の位置ロック

時計は Wallpaper Control で選択した言語に自動的に従います。

### 🖥️ システムモニター

システムウィジェットは、重要なハードウェアとシステム情報をデスクトップ上でひと目で確認できるようにします。

表示できる情報:

- CPU 使用率
- CPU 温度
- RAM 使用率
- GPU 使用率
- GPU 温度
- VRAM 使用率
- ネットワークのダウンロード通信
- ネットワークのアップロード通信
- ドライブ使用率

コンパクトなグラフィカル使用率バーを備え、**Minimal**、**Clean**、**Glow** のスタイルを選択できます。

ハードウェア監視は非同期で動作するため、センサー更新が壁紙トランジションやメインアプリの応答性を妨げません。

### 🌦️ 天気

天気ウィジェットは現在の気象情報をデスクトップ上に直接表示します。

表示できる情報:

- 現在の気温
- 体感温度
- 現在の天気
- 湿度
- 降水量
- 風速
- オプションの 3 日間予報

気象データは **Open-Meteo** から提供され、API キーは不要です。

場所は設定画面で指定でき、気象情報は選択した間隔で自動更新できます。

天気ウィジェットは **Minimal**、**Clean**、**Glow** のスタイルを選択でき、個別に配置・固定できます。

### 📅 カレンダー

カレンダーウィジェットは、今後の予定をデスクトップ上にコンパクトに表示します。

カレンダーデータは読み取り専用の **iCalendar / ICS** フィードから読み込まれます。

主な機能:

- 複数の ICS カレンダーソースに対応
- 時刻指定の予定
- 終日イベント
- 繰り返しイベント
- 複数日にまたがるイベント
- 複数日にまたがる終日イベントを対象となる各日に表示
- 同日の複数予定をまとめて表示
- 実際に予定がある今後の日だけを表示
- 予定のない日はスキップ
- イベント場所を任意で表示
- 表示する予定に応じてウィジェットサイズを自動調整
- 更新間隔を設定可能
- **Minimal**, **Clean** and **Glow** styles
- 個別の配置と位置ロック

プライベートな ICS アドレスは、現在の Windows ユーザー向けに **Windows Data Protection API (DPAPI)** を使用して暗号化保存されます。

Wallpaper Control はカレンダーフィードを読み取るだけで、カレンダーデータを変更しません。

#### 🎌 祝日カレンダー

個別の ICS ソースを祝日カレンダーとして設定できます。

祝日イベントは視覚的に強調され、同日の通常予定より前に自動配置されるため、祝日や特別なカレンダー項目を簡単に識別できます。

複数の通常カレンダーと祝日カレンダーを同じカレンダーウィジェットにまとめられます。

カレンダー更新は一時的なフィード障害にも耐えられる設計です。個別のソースが利用できなくなっても、以前に読み込んだイベントは表示されたままとなり、利用可能なフィードは更新を続けます。キャッシュされたデータが古い可能性がある場合は通知し、設定済みのすべてのフィードが正常に更新されると警告を解除します。キャッシュされたイベントは現在のアプリケーションセッション中保持されます。

### 次の壁紙

「次の壁紙」ウィジェットは、次の壁紙へ即座に進むためのコンパクトなデスクトップボタンです。

**Minimal**、**Clean**、**Glow** のスタイルを選択できます。変更はライブプレビューに即座に反映され、選択したスタイルはセッションをまたいで保存されます。既存の設定では従来の **Minimal** 表示が引き続き既定値として使用されます。

他のウィジェットとは独立して配置・固定でき、メインアプリと同じ壁紙切り替えおよびトランジション処理を使用します。

## 📊 統計

Wallpaper Control はスライドショーで表示された壁紙の統計を継続的に保存します。

統計ダッシュボードで表示できる情報:

- 各壁紙の総表示回数
- 各壁紙が最後に表示された日時
- 表示割合と人気順位
- 今日、昨日、過去 7 日間、過去 30 日間の統計
- 最も多く / 少なく表示された壁紙
- 平均表示回数
- 表示分布の均等性
- 平均再表示時間
- Top 10 チャート
- 一度も表示されていない、または長期間表示されていない壁紙

統計はローカルに保存され、アプリケーションを再起動しても保持されます。

Wallpaper Control は以前の統計ファイルをバックアップとして保持します。メインの統計ファイルを読み込めない場合は、破損ファイルを復旧用に残したまま自動的にバックアップへ切り替えられます。保存処理の競合や不完全な置換のリスクを減らすため、統計は一意の一時ファイルを経由して書き込まれます。

時間ベースの統計と再表示追跡は、対応する追跡データが初めて初期化された時点から開始します。それ以前の日別データや再表示履歴は再構築されません。

最近の壁紙履歴はセッション単位で管理され、Wallpaper Control を完全に終了すると消去されます。

## 🗑️ 壁紙の除外

現在表示されている壁紙が気に入りませんか？

Wallpaper Control はまず次の壁紙へ切り替え、変更が完了してから不要な画像を `Aussortiert` フォルダーへ移動します。壁紙トランジションの実行中は除外機能が一時的に無効となり、トランジション完了前に現在の画像が移動されることを防ぎます。

移動先は現在の壁紙フォルダー内、またはグローバルな除外フォルダーとして設定できます。

間違った画像を除外してしまった場合、現在のセッション中であれば直前の除外を元に戻せます。

## 🕹️ 外部制御

Wallpaper Control は実行中に外部アプリケーション、スクリプト、ショートカット、デスクトップツールからコマンドを受け取れます。

### 次の壁紙

次のコマンドに対応しています:

```text
WallpaperControl.exe --next
```

実行中の Wallpaper Control インスタンスへ要求を送り、すぐに次の壁紙へ切り替えます。

Wallpaper Control runs as a single instance for the current Windows user. Starting it again normally brings the existing main window to the foreground. Command communication such as `--next` is restricted to the current user, and malformed, oversized or stalled requests are rejected without blocking subsequent commands.

外部要求でも、Wallpaper Control から直接変更した場合と同じスライドショー、スケジュール、トランジション処理が使用されます。

メインウィンドウを開かずに、独自スクリプト、ランチャー、自動化ツール、その他のデスクトップアプリと Wallpaper Control を連携できます。

### 時計 Widget State

外部アプリケーションは次のレジストリキーを読み取ることで、Wallpaper Control のネイティブ時計が有効か確認できます:

```text
HKEY_CURRENT_USER\Software\WallpaperControl
```

レジストリ値:

```text
ClockWidgetEnabled
```

値:

```text
0 = ネイティブ時計ウィジェット無効
1 = ネイティブ時計ウィジェット有効
```

これにより、外部アプリやデスクトップツールは Wallpaper Control のネイティブ時計が有効かどうかに応じて動作を調整できます。

レジストリ値は保存済みのウィジェット設定を表します。設定ウィンドウを開いている間のプレビュー変更は、設定を保存するまで永続的には適用されません。

## 🩺 診断

Wallpaper Control には予期しないエラーや障害のための軽量な診断ログ機能があります。

ログは必要な場合のみ作成され、次の場所に保存されます:

```text
%APPDATA%\WallpaperControl\Logs
```

メインのログファイル:

```text
wallpaper-control.log
```

ログ機能はトラブルシューティング用で、通常の使用時に追加設定は必要ありません。

## 🌍 言語

Wallpaper Control は現在、次の言語に対応しています:

- 🇩🇪 ドイツ語
- 🇬🇧 英語
- 🇫🇷 フランス語
- 🇪🇸 スペイン語
- 🇯🇵 日本語

インターフェース言語はアプリケーション設定から直接変更できます。

デスクトップウィジェットのテキストと日付形式は選択したアプリケーション言語に従います。

## 💻 動作要件

- **Windows 11:** 対応・テスト済み
- **Windows 10:** 互換性が見込まれますが、現在は未テスト
- 64 ビット版 Windows
- .NET の個別インストールは不要

## 🚀 Installation

Wallpaper Control の**公式インストール方法**は、各リリースで提供される Windows x64 インストーラーです。

1. 最新の GitHub Release から `WallpaperControl-1.8.3-Setup-x64.exe` をダウンロードします。
2. インストールまたはアップグレード前に、システムトレイから既存の Wallpaper Control を完全に終了します。
3. インストーラーを実行します。
4. 必要に応じてセットアップ中にデスクトップショートカットを選択します。
5. スタートメニューまたはデスクトップショートカットから Wallpaper Control を起動します。

Wallpaper Control は現在の Windows ユーザー向けにインストールされ、**管理者権限は不要です**。

インストーラーには必要な **.NET ランタイム**が含まれているため、.NET を別途インストールする必要はありません。スタートメニューのショートカットは自動作成され、デスクトップショートカットは任意です。

既存のインストールをアップグレードする場合、Wallpaper Control の設定と統計は保持されます。自動起動がすでに有効な場合、そのエントリはインストール先のアプリケーションパスを使用するよう更新されます。

Wallpaper Control をアンインストールするとアプリケーションとショートカットは削除されますが、ユーザー設定と統計は保持され、後の再インストールで再利用できます。

現在、インストーラーは**デジタル署名されていません**。そのため、セットアップファイルの実行時に Windows がセキュリティ警告を表示する場合があります。

## 🔒 プライバシー

Wallpaper Control はアプリケーション設定と壁紙統計をコンピューター上にローカル保存します。

Wallpaper Control のアカウントは必要ありません。

壁紙管理、スライドショー制御、トランジション、フルスクリーン検出、統計など、ほとんどの機能は完全にローカルで動作します。

一部のオプション機能にはインターネット接続が必要です:

- **天気ウィジェット**は気象情報を取得するため Open-Meteo に接続します。
- **カレンダーウィジェット**は設定された iCalendar / ICS アドレスに接続してカレンダーデータを取得します。
- オプションの**アップデート確認**は GitHub Releases に接続し、新しい Wallpaper Control バージョンがあるか確認します。自動確認は無効化でき、Wallpaper Control がアップデートを自動でダウンロードまたはインストールすることはありません。

カレンダーウィジェットに設定されたプライベート ICS アドレスは、現在の Windows ユーザー向けに Windows Data Protection API (DPAPI) を使用して暗号化保存されます。

カレンダーへのアクセスは読み取り専用です。Wallpaper Control は予定やカレンダーデータを変更しません。

診断ログはローカルに保存され、トラブルシューティングが必要な場合にのみ作成されます。

## 🛠️ 使用技術

- C#
- .NET 10
- Windows Forms
- Native Windows APIs / COM integration
- Open-Meteo
- iCalendar / ICS

## 📄 ライセンス

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control は **GNU General Public License v3.0 (GPL-3.0)** の下で提供される、無料のオープンソースソフトウェアです。

GNU General Public License v3.0 の条件に従い、Wallpaper Control を自由に使用、研究、変更、再配布できます。

ライセンス全文は `LICENSE` ファイルを参照してください。

---

**Wallpaper Control**  
Windows がデスクトップに表示するものを、もう少し自分の手で。🖼️
