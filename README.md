# Open Monitor Manager

[日本語](#日本語) | [English](#english)

## 日本語

Windows向けのモニター調整ツールです。DDC/CIに対応するモニターの明るさ、コントラスト、RGBゲインを調整し、設定をプロファイルとして保存できます。画面の表示言語は英語です。

### 使い方

1. モニター本体の設定でDDC/CIを有効にします。対応する項目は機種や接続方法により異なります。
2. リポジトリ内の `OpenMonitorManager.exe` をダウンロードして起動します。同梱版はWindows x64向けです。管理者権限は要求しません。
3. `Monitor` で対象を選び、スライダーで調整します。対応していない項目は操作できません。
4. プロファイルの保存・適用・削除には、`Profile` 横のボタンを使います。保存対象は検出された全モニターの対応項目です。

ウィンドウを閉じると通知領域に常駐します。再表示はアイコンのダブルクリック、終了は通知領域メニューの `Exit` です。

### 接続の復旧と制限

スリープ復帰、ロック解除、スクリーンセーバー終了、画面構成の変更時にモニターを再検出します。設定変更に失敗した場合は再接続して1回再試行します。手動で再接続するには `Refresh / Reconnect` を使います。

- Windows DDC/CI経由の調整が対象です。WMI経由でのみ明るさを変更できるノートPC内蔵パネルには対応していません。
- RGBゲインが変更できない機種では、モニター側の画質・色温度設定をCustom/Userにする必要がある場合があります。
- RGBゲインはモニター本体の調整です。Windowsのガンマランプは変更しません。
- 対応機能や復帰の成否はモニター、GPU、ドックなどの接続環境に依存します。

### 保存データ

プロファイルは `profiles.json`、診断ログは `OpenMonitorManager.log` として次の場所に保存します。

```text
%LOCALAPPDATA%\OpenMonitorManager\
```

プロファイルには名前、モニター識別子、設定値が含まれます。ログにも機器情報やエラー時のパスが含まれる場合があるため、問題報告に添付する前に内容を確認してください。

### ソースからのビルド

Windowsと.NET 10 SDKが必要です。

```powershell
dotnet build ".\OpenMonitorManager.csproj" -c Release
dotnet run --project ".\OpenMonitorManager.csproj"
```

配布用の自己完結型・単一EXEを作成するには次を実行します。

```powershell
dotnet publish ".\OpenMonitorManager.csproj" -c Release
```

生成した `OpenMonitorManager.exe` はプロジェクト直下にコピーされます。中間生成物は `bin/`、`obj/`、`publish/` に保存されます。

## English

A monitor controller for Windows. Adjust brightness, contrast, and RGB gain through DDC/CI, and save settings as named profiles. The application UI is in English.

### Usage

1. Enable DDC/CI in your monitor's on-screen settings. Available controls depend on the monitor and connection.
2. Download and run `OpenMonitorManager.exe` from this repository. The included executable targets Windows x64 and does not request administrator privileges.
3. Select a display under `Monitor` and adjust the sliders. Unsupported controls are disabled.
4. Use the buttons next to `Profile` to save, apply, or delete a profile. Saving captures supported settings for all detected monitors.

Closing the window keeps the application running in the notification area. Double-click its icon to show the window, or select `Exit` from the tray menu to quit.

### Recovery and limitations

The application re-enumerates monitors after power resume, session unlock, screen saver exit, and display configuration changes. A failed adjustment triggers reconnection and one retry. Use `Refresh / Reconnect` to reconnect manually.

- Controls are limited to Windows DDC/CI. Laptop panels that expose brightness only through WMI are not supported.
- Some monitors require a Custom/User picture or color-temperature mode before accepting RGB gain changes.
- RGB gain changes the monitor's hardware controls, not the Windows gamma ramp.
- Supported controls and recovery behavior depend on the monitor, GPU, dock, and connection.

Monitor discovery uses `user32.dll` and `dxva2.dll`, with EDID names and Windows device identifiers used to describe displays. Slider writes are debounced, and slow DDC/CI calls run away from the UI thread. Duplicate instances are prevented within the Windows session.

### Stored data

Profiles (`profiles.json`) and diagnostic logs (`OpenMonitorManager.log`) are stored under:

```text
%LOCALAPPDATA%\OpenMonitorManager\
```

Profiles contain names, monitor identifiers, and settings. Logs may also contain device information and paths from errors. Review these files before attaching them to a public issue.

### Build from source

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet build ".\OpenMonitorManager.csproj" -c Release
dotnet run --project ".\OpenMonitorManager.csproj"
```

Create a self-contained, single-file executable:

```powershell
dotnet publish ".\OpenMonitorManager.csproj" -c Release
```

The resulting `OpenMonitorManager.exe` is copied to the project root. Build and publish intermediates remain under `bin/`, `obj/`, and `publish/`.

## License / ライセンス

[MIT](LICENSE)
