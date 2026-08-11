# Files アーキテクチャ

`Files` は、旧 `Files.App` のサービスロケーター、設定サービス、legacy storage shapeを持ち込まずに
`Files.Core`をWinUIへ接続するための移行ホストです。旧アプリは機能互換性を保つために残しますが、新しい `Files` 機能は旧経路へ依存しません。

## 依存方向と所有権

```mermaid
flowchart TB
    App["Files App"]
    Window["MainWindow / RootView"]
    Factory["WindowPresentationFactory"]
    ViewModel["RootViewModel"]
    Commands["CommandRegistry / WindowCommandManager"]
    Tabs["TabView"]
    Navigation["NavigationToolbar"]
    Shell["native NavigationView"]
    Toolbar["ToolbarView"]
    Panes["PaneHost"]
    Pane["PaneView / PaneContentView"]
    Browser["FolderBrowser"]
    Adapter["BrowsePresentationAdapter"]
    Runtime["FilesCoreRuntime"]
    Models["ShellSession / Window / Tab / Pane"]
    Session["BrowseSession"]
    Source["Windows storage source"]

    App --> Runtime
    App --> Window
    Window --> Factory
    Factory --> ViewModel
    ViewModel --> Tabs
    ViewModel --> Navigation
    ViewModel --> Commands
    ViewModel --> Shell
    Commands --> Navigation
    Commands --> Toolbar
    Commands --> Tabs
    Commands --> Panes
    Shell --> Toolbar
    Shell --> Panes
    Panes --> Pane
    Pane --> Browser
    Browser --> Adapter
    Adapter --> Models
    Runtime --> Models
    Models --> Session
    Session --> Source
```

`App`はprocess scopeの`FilesCoreRuntime`を所有します。`MainWindow`は`WindowPresentationFactory`でCoreの`WindowSession`とWorkspaceをUIへ合成し、
`RootView`へ完成済みの`RootViewModel`を渡します。`BrowsePresentationAdapter`が`BrowsePaneSession`の状態をUI向けsnapshotへ変換します。
window終了時は`RootView`の購読を先に解除し、その後runtimeを非同期破棄します。

## UIコントロール階層

`Files` の Shell は、旧 `MainPage` へ機能を集約せず、Core の所有階層に対応する独立した Control へ分割します。

```mermaid
flowchart TB
    Root["RootView"]
    Tabs["TabView<br/>AppWindow title bar"]
    Navigation["NavigationToolbar"]
    Sidebar["native NavigationView"]
    Toolbar["ToolbarView"]
    Panes["PaneHost<br/>ItemsRepeater"]
    Pane["PaneView<br/>PaneContentView"]
    Browser["FolderBrowser<br/>ContentPresenter"]
    Details["DetailsFolderView"]
    Grid["GridFolderView"]
    List["ListFolderView"]
    Terminal["TerminalView"]
    Info["InfoPane"]

    Root --> Tabs
    Root --> Navigation
    Root --> Sidebar
    Sidebar --> Toolbar
    Sidebar --> Panes
    Sidebar --> Terminal
    Sidebar --> Info
    Panes --> Pane
    Pane --> Browser
    Browser --> Details
    Browser --> Grid
    Browser --> List
```

- `RootView`はwindow単位の`TabView`、`NavigationToolbar`、native `NavigationView`の接続だけを担当します。
- `TabView`はWinUI 3の`AppWindow.TitleBar`/`Window.SetTitleBar`を所有し、tab stripだけを描画します。
- `NavigationToolbar`はwindowに1つだけ存在し、`RootViewModel`のcommand bindingとactive browserへ接続します。
- `RootView`は`NavigationView`を直接宣言します。NavigationViewのContentに`ToolbarView`、`PaneHost`、
  `TerminalView`、`InfoPane`、status surfaceを配置します。
- `PaneHost`は`LeftPane`/`RightPane`の固定プロパティを持たず、`TabViewModel.Panes`を`ItemsRepeater`で描画します。
  Core `TabSession`は1..2 paneを所有し、UI側はterminalなどの複数paneレイアウトへ置き換えられる境界を維持します。
- `PaneHost` は `ScrollViewer` で `ItemsRepeater` を包みません。分割方向に応じて `ItemsRepeater` の layout を切り替え、各 `PaneView` を利用可能な幅または高さへ stretch します。
- `PaneView` は pane ごとの表示 surface を所有します。`PaneContentView` は `PaneSession.Content` に対応する表示モデルを `ContentPresenter` へ渡し、スクロールや Core navigation を持ちません。
- 現在の factory は `BrowsePaneSession` を `FolderBrowserViewModel` へ写像します。Settings や Web を追加する場合は enum だけを増やさず、対応する `IPaneContentSession` と所有ライフタイムを先に実装します。
- `FolderBrowser`は表示モードのhostです。現在は`DetailsFolderView`を既定にし、`GridFolderView`と`ListFolderView`を
  同じ`ContentPresenter`へ差し替え、Card/Columns viewを同じ境界へ追加できます。
- controlはCore modelを直接XAMLへ公開せず、`RootViewModel`/`TabViewModel`/`PaneViewModel`/
  `FolderBrowserViewModel`だけをバインド対象にします。
- `DetailsFolderView` は `ListView` の標準選択状態を視覚的な selection indicator として使い、選択チェックボックスを表示しません。各項目は Core の thumbnail projection を表示します。

各 `UserControl` は `ViewModel` を dependency property で受け取ります。Control の内部挙動を ViewModel へ移したり、Control ごとにサービスを解決したりしません。

## コマンド登録と実行

`src/Files/Commands/` は、`Files` 専用の最初のコマンド境界です。

- `AppCommandRegistration.Build()`は`App`のcomposition rootで一度だけ呼び出され、stable `CommandId`を
  `CommandRegistryBuilder`へ明示登録します。
- `CommandRegistry`はimmutableなprocess-level catalogです。各`MainWindow`はそれから独立した
  `WindowCommandManager`を作成します。
- `RootViewModel`はwindow managerとcommand bindingを所有し、`NavigationToolbar`、`ToolbarView`、`TabView`、
  sidebarのHome、folder double-clickへ同じcommand surfaceを渡します。
- 基本登録対象はback/forward/up/home/path/refresh/open item、新規・終了tab、新規・終了paneです。
  storage operation、shortcut、localization、plugin commandは後続sliceで追加します。
- handlerは`Files.Core`へ直接WinUI型を持ち込まず、`Files` ViewModelとCore adapterを通して実行します。

## 基本 browsing 垂直スライス

- `FilesCoreBuilder.AddWindowsStorage()`でWindows sourceを合成する。
- Homeと`file:` rooted folderを`BrowsePaneSession`へnavigateする。
- Coreのgeneration/items version付き状態をUI dispatcherへsnapshotする。
- `StorableReference`/`StorableKey`を保持した表示項目を作る。
- command registryを通してback/forward/up、refresh、path navigation、複数選択、folder double-clickをCoreへroutingする。

CoreイベントはUI threadで発生するとは限らないため、adapterはCore項目をそのままWinUI collectionへ渡しません。
snapshotのversionを検査し、DispatcherQueue上で一覧とselectionを更新します。Coreが所有するmodelの破棄は
runtimeとpaneに任せ、UI項目は参照値だけを保持します。

## Filesに入れないもの

旧設定サービス、`Ioc.Default`、legacy `ListedItem`、Frame履歴、旧command registryは`Files`の新しい依存にしません。
preview、操作dialog、activation、localization、Search/Library/Tag/FTPは、対応するCore契約とUI adapterを
別のvertical sliceとして追加します。

## デバッグとベンチマーク

手動の起動/Home/folder navigation/selection/refresh/終了スモークと、Debugでのstartup/navigation/refresh計測は
[テストと性能](testing.md)に定義します。DebugのUI計測は退行検出用です。比較可能な性能値は同一fixtureを使った
Releaseの`Files.Benchmarks`で取得し、Shellやディスクの遅延をCoreのマイクロベンチマークへ混ぜません。
