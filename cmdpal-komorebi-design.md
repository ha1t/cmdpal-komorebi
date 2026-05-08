# CmdPal Komorebi Window Switcher 設計メモ

## 1. プロジェクト概要

PowerToys Command Palette (CmdPal) の拡張機能として、komorebi が管理しているウィンドウのリストから絞り込み検索 → Enter で即フォーカス、を実現する。

`Win+Alt+Space` → "window" などでフィルタ → 任意のウィンドウへジャンプ、をワンアクションで。

### ゴール (MVP)

1. 全ワークスペース横断のウィンドウ一覧を CmdPal の ListPage に表示
2. タイトル / EXE 名でインクリメンタル検索
3. Enter で対象ウィンドウのワークスペースに切り替え + ウィンドウフォーカス
4. ウィンドウアイコンを EXE から取得して表示

### ノンゴール (将来検討)

- マルチモニター対応 (現在は 1 モニターのみで運用、Scrolling layout も単一モニター前提)
- ウィンドウ操作 (close, minimize, swap など) — フォーカスのみに集中
- カスタムソート (LRU など)
- Floating window のハンドリング

---

## 2. 環境前提

- **OS**: Windows 11 (Developer Mode 有効必須 — 設定 → システム → 開発者向け)
- **WM**: komorebi v0.1.38+ (Scrolling layout 対応版) — 検証環境は v0.1.41
- **PowerToys**: 0.98.1+ / Command Palette (`Microsoft.CommandPalette` 0.9+) 有効
- **モニター構成**: 1 枚のみ (multi-monitor は対象外)
- **ワークスペース構成**: 7 ワークスペース (I〜VII)
  - I: VerticalStack
  - II〜VI: Scrolling (2 or 3 columns)
  - VII: RightMainVerticalStack

### 開発環境

- Visual Studio 2022 17.14+ (Windows App SDK / WinUI ワークロード)
- .NET 9 SDK (CmdPal v0.9 テンプレの TargetFramework は `net9.0-windows10.0.26100.0`)
- C# / Windows App SDK 1.8
- Windows 11 SDK 10.0.26100
- 主要 NuGet: `Microsoft.CommandPalette.Extensions` (v0.9.260303001 系) ← Toolkit 同梱、`Microsoft.WindowsAppSDK`、`Shmuelie.WinRTServer`

---

## 3. アーキテクチャ

```
┌─────────────────────────────────────┐
│  Command Palette (cmdpal.exe)       │
│                                     │
│  ┌──────────────────────────────┐  │
│  │ KomorebiWindowsExtension      │  │
│  │  ├─ TopLevelCommand           │  │
│  │  └─ KomorebiWindowsPage   │  │
│  └──────┬───────────────────────┘  │
└─────────┼───────────────────────────┘
          │ Process.Start
          ▼
   ┌──────────────┐
   │  komorebic   │ ──── stdout JSON ───┐
   └──────┬───────┘                     │
          │ UDS                         │
          ▼                             │
   ┌──────────────┐                     │
   │   komorebi   │ ◀───────────────────┘
   └──────────────┘
```

### MVP の通信戦略

シンプルに `Process.Start("komorebic.exe", "...")` を毎回呼ぶ。理由:

- 実装が単純 (TCP/Named Pipe のセットアップ不要)
- 起動オーバーヘッドは数十 ms 程度で UX に許容範囲
- Page を開くたびに最新状態が取れる (キャッシュ無効化問題なし)

将来の最適化: パフォーマンスに不満が出たら TCP リスナー (`komorebi --tcp-port=N`) 経由 or Named Pipe subscribe に切り替え。

---

## 4. komorebic との連携仕様

### 4.1 ウィンドウ一覧取得

**コマンド**: `komorebic state`

理由: `visible-windows` だと「現在画面に見えているウィンドウ」だけになる (Scrolling layout で off-screen のウィンドウが除外される)。全ワークスペース横断で全ウィンドウを取りたいので `state` を使う。

**出力構造 (抜粋)**:

```json
{
  "monitors": {
    "elements": [
      {
        "id": 12345,
        "name": "MONITOR_NAME",
        "workspaces": {
          "elements": [
            {
              "name": "I",
              "containers": {
                "elements": [
                  {
                    "windows": {
                      "elements": [
                        {
                          "hwnd": 131444,
                          "title": "...",
                          "exe": "Code.exe",
                          "class": "Chrome_WidgetWin_1",
                          "rect": { ... }
                        }
                      ]
                    }
                  }
                ]
              },
              "floating_windows": { "elements": [...] }
            }
          ],
          "focused": 0
        }
      }
    ],
    "focused": 0
  }
}
```

**注意点**:

- `state` の JSON は実環境で叩いて確認すること (バージョン差異あり)
- `containers.elements[].windows.elements[]` がタイル管理されているウィンドウ
- `floating_windows.elements[]` も拾う (フローティングは別構造)
- workspace_index は `workspaces.elements` 配列のインデックス (0-indexed)

### 4.2 フォーカス操作

選択時の動作:

1. `komorebic focus-workspace <workspace_index>` で対象ワークスペースに切り替え
2. ウィンドウが現在のフォーカス対象でない場合、該当ウィンドウに直接フォーカスする必要あり

**ウィンドウフォーカスの方法**:

最もシンプルかつ確実なのは Win32 API の `SetForegroundWindow(hwnd)` を P/Invoke で呼ぶこと。

理由:
- komorebic には「特定 hwnd にフォーカス」する直接コマンドがない (`focus left/right/up/down` は方向ベース)
- Scrolling layout は **フォーカスベースで自動スクロール** するため、SetForegroundWindow でフォーカスが移れば layout 側が勝手に表示位置を調整してくれる
- ワークスペース切り替えと組み合わせれば、どのワークスペースのどのウィンドウにも到達可能

```csharp
[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
```

**順序**:

```csharp
// 1. ワークスペース切り替え (アニメーション 400ms 待つ必要あり?)
RunKomorebic($"focus-workspace {targetWsIndex}");

// 2. 少し待つ (komorebi の hide/cloak が解除されるまで)
await Task.Delay(50);

// 3. ウィンドウフォーカス
ShowWindow(hwnd, SW_RESTORE);  // 9
SetForegroundWindow(hwnd);
```

**SetForegroundWindow の制約**:

Windows のフォアグラウンド制限を回避するため、`AllowSetForegroundWindow` や、`AttachThreadInput` を使ったハックが必要になることがある。最初は単純実装で試して、効かない場合に追加対策する。

---

## 5. Command Palette 拡張の構造

### 5.1 プロジェクト生成

CmdPal 自体から:

```
Win+Alt+Space → "Create a new extension" → フォーム入力
  ExtensionName: KomorebiWindows
  Display Name: Komorebi Windows
  Output Path: <このリポジトリのルート>
```

生成されるファイル構成 (公式テンプレ 2026-04 時点):

```
KomorebiWindows/
├── Directory.Build.props
├── Directory.Packages.props
├── nuget.config
├── KomorebiWindows.sln
└── KomorebiWindows/
    ├── app.manifest
    ├── Package.appxmanifest
    ├── Program.cs
    ├── KomorebiWindows.cs
    ├── KomorebiWindows.csproj
    ├── KomorebiWindowsCommandsProvider.cs
    ├── Assets/                       ← プレースホルダ画像
    ├── Pages/
    │   └── KomorebiWindowsPage.cs
    └── Properties/
        ├── launchSettings.json
        └── PublishProfiles/
            ├── win-arm64.pubxml
            └── win-x64.pubxml
```

### 5.2 主要クラス

#### TopLevelCommand (エントリーポイント)

```csharp
public sealed class KomorebiWindowsCommandsProvider : CommandProvider
{
    public KomorebiWindowsCommandsProvider()
    {
        DisplayName = "Komorebi Windows";
        Icon = IconHelpers.FromRelativePath("Assets\\komorebi.png");
    }

    private readonly ICommandItem[] _commands = [
        new CommandItem(new KomorebiWindowsPage())
        {
            Title = "Komorebi: Switch Window",
            Subtitle = "List all windows managed by komorebi",
        }
    ];

    public override ICommandItem[] TopLevelCommands() => _commands;
}
```

#### ListPage (ウィンドウ一覧)

```csharp
internal sealed partial class KomorebiWindowsPage : ListPage
{
    public KomorebiWindowsPage()
    {
        Icon = new IconInfo("\uE8A7");  // SwitchApps icon
        Title = "Komorebi Windows";
        Name = "Switch";
        PlaceholderText = "Search windows by title or app name...";
        ShowDetails = false;
    }

    public override IListItem[] GetItems()
    {
        var state = KomorebiClient.GetState();
        var items = new List<IListItem>();

        foreach (var (mon, monIdx) in state.Monitors.Select((m, i) => (m, i)))
        {
            foreach (var (ws, wsIdx) in mon.Workspaces.Select((w, i) => (w, i)))
            {
                // タイル管理されているウィンドウ
                foreach (var container in ws.Containers)
                {
                    foreach (var window in container.Windows)
                    {
                        items.Add(BuildItem(window, monIdx, wsIdx, ws.Name));
                    }
                }
                // フローティングウィンドウ
                foreach (var window in ws.FloatingWindows)
                {
                    items.Add(BuildItem(window, monIdx, wsIdx, ws.Name, isFloating: true));
                }
            }
        }

        return items.ToArray();
    }

    private ListItem BuildItem(KomorebiWindow w, int monIdx, int wsIdx,
                                string wsName, bool isFloating = false)
    {
        var subtitle = isFloating
            ? $"{w.Exe} · WS {wsName} · floating"
            : $"{w.Exe} · WS {wsName}";

        return new ListItem(new FocusWindowCommand(w.Hwnd, wsIdx))
        {
            Title = w.Title,
            Subtitle = subtitle,
            // exe path は state JSON に含まれないので hwnd → PID → QueryFullProcessImageName で解決
            Icon = IconHelpers.FromExecutablePath(Win32.GetExePathFromHwnd(w.Hwnd) ?? w.Exe),
        };
    }
}
```

#### FocusWindowCommand

```csharp
internal sealed class FocusWindowCommand : InvokableCommand
{
    private readonly long _hwnd;
    private readonly int _workspaceIdx;

    public FocusWindowCommand(long hwnd, int workspaceIdx)
    {
        _hwnd = hwnd;
        _workspaceIdx = workspaceIdx;
        Name = "Focus";
        Icon = new IconInfo("\uE8A7");
    }

    public override ICommandResult Invoke()
    {
        // 1. ワークスペース切り替え
        KomorebiClient.RunCommand($"focus-workspace {_workspaceIdx}");

        // 2. 少し待つ (cloak 解除のため)
        Thread.Sleep(50);

        // 3. ウィンドウフォーカス (Win32 API)
        Win32.ShowWindow((IntPtr)_hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow((IntPtr)_hwnd);

        return CommandResult.Hide();
    }
}
```

#### KomorebiClient (komorebic ラッパー)

```csharp
internal static class KomorebiClient
{
    public static KomorebiState GetState()
    {
        var psi = new ProcessStartInfo("komorebic.exe", "state")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return JsonSerializer.Deserialize<KomorebiState>(json, JsonOpts)!;
    }

    public static void RunCommand(string args)
    {
        var psi = new ProcessStartInfo("komorebic.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(2000);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
```

#### Win32 P/Invoke

```csharp
internal static class Win32
{
    public const int SW_RESTORE = 9;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess, uint flags, System.Text.StringBuilder buf, ref uint size);

    /// <summary>hwnd → 実行ファイルのフルパス。失敗時は null。</summary>
    public static string? GetExePathFromHwnd(long hwnd)
    {
        GetWindowThreadProcessId((IntPtr)hwnd, out var pid);
        if (pid == 0) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
```

---

## 6. データモデル

`komorebic state` の JSON を C# レコードにマッピング。**実際のスキーマは `komorebic state | jq` で確認してから確定すること**。

```csharp
internal record KomorebiState
{
    [JsonPropertyName("monitors")]
    public Ring<Monitor> Monitors { get; init; } = new();
}

internal record Ring<T>
{
    [JsonPropertyName("elements")]
    public List<T> Elements { get; init; } = new();

    [JsonPropertyName("focused")]
    public int Focused { get; init; }
}

internal record Monitor
{
    public string Name { get; init; } = "";
    public Ring<Workspace> Workspaces { get; init; } = new();
}

internal record Workspace
{
    public string Name { get; init; } = "";
    public Ring<Container> Containers { get; init; } = new();

    [JsonPropertyName("floating_windows")]
    public Ring<KomorebiWindow> FloatingWindows { get; init; } = new();
}

internal record Container
{
    public Ring<KomorebiWindow> Windows { get; init; } = new();
}

internal record KomorebiWindow
{
    public long Hwnd { get; init; }
    public string Title { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Class { get; init; } = "";
    // 注意: komorebic state v0.1.41 の windows.elements[] には `path` フィールドは無い。
    // exe フルパスが必要なら hwnd から GetWindowThreadProcessId →
    // OpenProcess → QueryFullProcessImageName で解決する。
}
```

---

## 7. 開発手順

### Step 1: 環境確認 (5 min)

```pwsh
komorebic --version          # v0.1.38 以上か
komorebic state | Out-File -Encoding utf8 state-sample.json
# state-sample.json を眺めて実際のスキーマを確認
```

### Step 2: プロジェクト雛形生成 (5 min)

CmdPal から `Create a new extension` を実行してテンプレ生成。

**注意**: CmdPal v0.9 のテンプレは `.gitignore` を生成しない。リポジトリ既存の `.gitignore` がデフォルト C# 用 (`launchSettings.json` や `*.pubxml` を除外する) になっている場合のみ、それらの行を削除する作業が必要。新規リポジトリで `.gitignore` を後から追加する場合は、`launchSettings.json` と `*.pubxml` を除外しないようにする。

(参考: Microsoft Learn `creating-an-extension`)

### Step 3: KomorebiClient 実装 + 単体確認 (30 min)

`KomorebiClient.GetState()` を実装し、Console.WriteLine でデシリアライズ結果を確認。
Visual Studio から Console プロジェクトを別途作って動作検証するのが早い。

### Step 4: ListPage 実装 (60 min)

ウィンドウを列挙してリスト表示。アイコンなしの素のリストでまず動かす。

### Step 5: フォーカス処理実装 (30 min)

`FocusWindowCommand.Invoke()` でワークスペース切替 + SetForegroundWindow。

### Step 6: アイコン取得 (30 min)

`IconHelpers.FromExecutablePath()` または独自で `ExtractIconEx` で EXE からアイコン抽出。

### Step 7: デプロイ + リロード確認

```
Visual Studio → 右クリック → Deploy
CmdPal で "Reload" コマンド (subtitle: Reload Command Palette Extension)
```

### Step 8: 動作確認チェックリスト

- [ ] `Win+Alt+Space` → "Komorebi" でコマンドが出る
- [ ] 選択するとウィンドウ一覧が表示される
- [ ] タイトル/EXE で絞り込みできる
- [ ] 同じワークスペース内のウィンドウへ Enter でフォーカスが切り替わる
- [ ] 別ワークスペースのウィンドウへ Enter でワークスペース切替+フォーカス
- [ ] Scrolling layout で off-screen のウィンドウへもフォーカスできる
- [ ] Floating ウィンドウもリストに含まれてフォーカスできる
- [ ] 各アイテムにアプリのアイコンが表示される

---

## 8. 既知のリスク・要検証項目

### R1: `komorebic state` の JSON スキーマがバージョン依存

→ **対策**: 実環境で `komorebic state` を 1 度叩いて構造を確認してから型定義を確定する。`Ring<T>` 型 (focused + elements) は yasb の事例から推定だが、実物で要確認。

### R2: SetForegroundWindow が効かないケース

Windows のフォアグラウンドロック制限により、別プロセスからの呼び出しが無視されることがある。

→ **対策**:
1. まず素朴に試す
2. 効かない場合は `AllowSetForegroundWindow(ASFW_ANY)` を先に呼ぶ
3. それでも効かない場合は `AttachThreadInput` ハックを使う

### R3: ワークスペース切替直後の hwnd が無効

`window_hiding_behaviour: Cloak` なので Hide ではなく Cloak されるが、切替直後すぐの SetForegroundWindow は失敗する可能性。

→ **対策**: 50ms の sleep を入れる。それでもダメなら komorebi の `FocusChange` イベントを subscribe して切替完了を待つ。

### R4: アニメーション (400ms) との干渉

komorebi.json で `animation.duration: 400` なので、切替アニメ中にフォーカス命令を出すと不自然な挙動になる可能性。

→ **対策**: 必要なら 400ms 待ってからフォーカスする。MVP では 50ms で試して挙動を見る。

### R5: komorebic.exe の PATH 解決

CmdPal は MSIX で動くため、ユーザーの PATH が通っているかは別問題。

→ **対策**:
1. まず `komorebic.exe` で叩いて、PATH が通っているか確認
2. ダメなら `%USERPROFILE%\.cargo\bin\komorebic.exe` などの候補をフォールバック
3. 設定で絶対パス指定できるようにする (将来対応)

### R6: タイトルが空のウィンドウのフィルタ

`hwnd` だけあって `title` が空のシステムウィンドウが state に混じることがある。

→ **対策**: `string.IsNullOrWhiteSpace(title)` のものは除外。

---

## 9. 参考リンク

- [Command Palette extension development](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extension-development)
- [Creating an extension (step-by-step)](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension)
- [Command Palette samples](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/samples)
- [SamplePagesExtension (GitHub, microsoft/PowerToys)](https://github.com/microsoft/PowerToys/tree/main/src/modules/cmdpal/ext/SamplePagesExtension) — `MyPage` が ListPage の最小例
- [CmdPal-Extensions community gallery](https://github.com/microsoft/CmdPal-Extensions)
- [komorebic CLI reference](https://komorebi.lgug2z.com/reference/komorebic-windows/)
- [komorebi Named Pipe subscription](https://github.com/LGUG2Z/komorebi#window-manager-event-subscriptions)
- [awesome-komorebi](https://github.com/LGUG2Z/awesome-komorebi)

---

## 10. Claude Code への引き継ぎプロンプト案

```
このディレクトリで PowerToys Command Palette 拡張機能を作りたい。
仕様は cmdpal-komorebi-design.md を参照。

最初のステップとして:
1. komorebic state の出力を実際に取得して、想定スキーマと
   一致しているか確認したい。Step 1 を実行して state-sample.json
   を作成し、構造を確認してほしい。
2. 想定スキーマ (設計メモ Section 6) と差分があれば指摘してほしい。
3. その後、Step 2 (プロジェクト雛形生成) に進みたい。

私のマシンは Windows 11、PowerShell 7、Visual Studio 2022 が
インストール済み。komorebi も動作中。
```
