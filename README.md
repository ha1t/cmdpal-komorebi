# cmdpal-komorebi

[komorebi](https://github.com/LGUG2Z/komorebi) が管理しているウィンドウを [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview) から検索 → フォーカスする拡張機能。

`Win+Alt+Space` → `Komorebi` → タイトル / EXE 名で絞り込み → Enter で対象ウィンドウのワークスペースに切り替え + フォーカス。Scrolling layout で off-screen のウィンドウもワンアクションで呼び出せる。

## 機能

- 全ワークスペース横断のウィンドウ一覧
- タイトル / EXE 名でインクリメンタル検索 (CmdPal 標準)
- Enter で `komorebic focus-workspace` + `SetForegroundWindow` の組み合わせでフォーカス
- 各アイテムに EXE アイコン表示 (hwnd → PID → `QueryFullProcessImageName` で解決)

## 必要環境

- Windows 11
- [PowerToys](https://learn.microsoft.com/windows/powertoys/install) 0.98+ (Command Palette 有効)
- [komorebi](https://github.com/LGUG2Z/komorebi) v0.1.38+ (動作確認は v0.1.41)
- ビルド時: Visual Studio 2022 17.14+ (Windows App SDK / WinUI ワークロード) + Windows 11 SDK 10.0.26100 + [Developer Mode 有効](https://learn.microsoft.com/windows/apps/get-started/enable-your-device-for-development)

## インストール (バイナリ配布版)

ビルド環境を用意したくない人向け。[Releases](../../releases) から `.msix` と `.cer` をダウンロードしてください。

1. このプロジェクトの **`dist/KomorebiWindows.cer`** を「信頼された発行元」にインポート (**管理者 PowerShell** で実行):
   ```powershell
   Import-Certificate -FilePath .\KomorebiWindows.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
2. `KomorebiWindows_<version>_x64.msix` をダブルクリック → 「インストール」
3. Command Palette で `Reload Command Palette Extension` を実行
4. `Win+Alt+Space` → `Komorebi` で起動

> `.cer` は self-signed (CN=ha1t) です。インポートすると `ha1t` が署名した MSIX を信頼することになります。気になる場合はソースからビルドしてください。

## ソースからビルド

1. このリポジトリを clone
2. Visual Studio 2022 で `KomorebiWindows/KomorebiWindows.sln` を開く
3. ソリューションエクスプローラで `KomorebiWindows` プロジェクトを右クリック → **配置 (Deploy)**
4. Command Palette を開いて `Reload` で **Reload Command Palette Extension** を実行
5. `Win+Alt+Space` → `Komorebi` で起動

> 初回ビルド時に VS が `KomorebiWindows_TemporaryKey.pfx` を要求します。プロジェクト内に既存の pfx (CN=ha1t) があればそれを使うか、`Package.appxmanifest` のパッケージ化タブで自分の証明書を新規作成してください。

## 使い方

1. `Win+Alt+Space` で Command Palette を開く
2. `Komorebi` で検索 → **Komorebi Windows** を選択
3. ウィンドウ一覧が出るので、タイトル or EXE 名で絞り込み
4. Enter で対象ウィンドウにフォーカス (別ワークスペースなら自動で切り替わる)

## 設計

仕様・データモデル・判断のトレードオフは [`cmdpal-komorebi-design.md`](./cmdpal-komorebi-design.md) を参照。

## 既知の制限 / ノンゴール

- マルチモニター: 現状 1 モニター運用前提 (Scrolling layout も単一モニター前提)
- ウィンドウ操作 (close / minimize / swap など) は未対応 — フォーカスのみに集中
- LRU 等のカスタムソート未対応
- `SetForegroundWindow` のフォアグラウンドフォーカス制限に当たるケースのフォールバック (`AllowSetForegroundWindow` / `AttachThreadInput`) は未実装

## ライセンス

[MIT License](./LICENSE) — 詳細はファイルを参照。
