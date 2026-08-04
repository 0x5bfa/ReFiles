# Files.Core / Files 移行進捗

この文書だけが、Files.Core への移行と Files の実装進捗を記録します。
その他の architecture 文書は、完了状況ではなく設計上の概念、契約、所有権、境界を定義します。

## 完了した境界

`Files.Core` はUI非依存のstorage、capability、operation、項目 AppModel、Shell Sessionを提供します。

| 領域 | 状況 |
| --- | --- |
| Shell Session | application、window、tab、1..2 pane、typed pane content、history、preview、browse sessionを実装済み |
| Windows storage | resolve、enumeration、stable reference、property、thumbnail、change sourceを実装済み |
| FTP storage | FTP/FTPS source、stream、property、operationを実装済み |
| browse | context ownership、atomic navigation/refresh、incremental reconciliation、selection、projectionを実装済み |
| operation | create、rename、copy、move、deleteとcollision policyを実装済み |
| preview | stream previewとWindows Shell Preview Handler sessionを実装済み |
| threading | ordered/concurrent/operation用message-pumped Shell STAを実装済み |
| validation | Files.Core unit/integration testとbenchmark projectが存在 |

`Files` の最初の browsing slice は、次の境界まで接続済みです。

- `MainWindow`から`RootView`を起点に、custom `TabView`、window単位の`NavigationToolbar`、native `NavigationView`、
  `ToolbarView`、`PaneHost`、`PaneView`、`PaneContentView`、`FolderBrowser`、`DetailsFolderView`を独立したcontrolとして構成する。
- custom `TabView`はWinUI 3 title bar APIを使い、`PaneHost`は`Panes` collectionをItemsRepeaterへ投影する。
- `DetailsFolderView`は現在stable key selectionをCoreへroutingするListView実装で、表示モードの差し替え境界を提供する。
- 起動時に `FilesCoreRuntime` を1つ作成し、最終終了時に非同期破棄する。
- `FilesApplicationSession` が作成した `WindowSession` の active `TabSession`/`PaneSession` をUI adapterへ渡す。
- Home と rooted local Windows folderをCoreでresolve、enumerate、watch、refreshする。
- Coreのversion付き一覧をDispatcherQueue上で`Files`のpresentation collectionへ投影する。
- selectionをstable keyでCoreへ送り、Coreのselection stateをUIへreconcileする。
- back/forward/up、path navigation、refreshを `PaneSession` へroutingする。
- `AppCommandRegistration`でstable command IDをprocess-level registryへ登録し、window単位の
  `WindowCommandManager`からnavigation、tab、pane、Home、folder double-clickを実行する。

## 基本 browsing の完了条件

次の操作は `Files.Core` の `WindowSession`、`TabSession`、`PaneSession`、`BrowseSession` を正本として
実行されます。

- tab の作成、選択、終了。
- pane の作成、active pane の切り替え、終了。
- back、forward、up、Home、path navigation、refresh。
- folder の double-click、stable-key selection、selection の再同期。
- Core event の dispatcher 越しの snapshot 適用と、tab selection の安定性。

Core の `TabSession` は現在 1..2 pane を所有します。`Files` の `PaneHost` は collection 境界を維持しますが、
3 pane 以上のレイアウトは別の Core 拡張です。

## 次の移行単位

1. Details viewをList/Grid/Card/Columnsへ拡張し、view settingsとviewport reportingを接続する。
2. `Files`へ preview UIをCore `PaneSession.Preview` とWindows Shell preview sessionへ接続する。
3. delete/copy/move/createをCore operation requestへ移し、既存dialog、進行状況、elevation、server継続をadapter化する。
4. Search/Library/Tag/FTPを型付き `BrowseLocation` とCore sourceへ移す。
5. `Files`のWinUI presentation model、localization、activation、永続化を追加する。
6. 対応する `Files` slice が移行された後、旧 `Files.App` の互換経路を機能単位で削除する。

## Trickle-down MVVM の残作業

最初の browsing slice は動作境界を優先したため、入れ子 ViewModel に `IStorageWorkspace`、dispatcher、command manager が引き継がれています。
次の UI slice では、[Trickle-down MVVM の設計規約](trickle-down-mvvm.md)に従って次を完了条件にします。

- `RootViewModel` が作った UI adapter/presenter factory を必要な View 層へ明示的に渡す。
- `TabViewModel`、`PaneViewModel`、`FolderBrowserViewModel` が runtime や data root を直接受け取らない。
- Control は dependency property で ViewModel を受け取り、Control の内部挙動を新しい ViewModel へ逃がさない。
- pane の layout、scroll、content selection、folder view の selection/thumbnail をそれぞれの View 境界に固定する。

## 旧 Files.App の互換経路

次は機能を失わないために既存Files.App経路を維持しています。

- Home、Search、Library、Tag、FTPの画面navigationとitem presentation
- Frame back/forward、tab session persistence、toolbar command state
- delete、copy、move、create、clipboard、drag/drop、context menu、sharing
- preview paneのWinUI hostと既存preview routing
- Recycle Bin watcher、drive monitoring、taskbar progress、object picker
- 旧WinRT FTP itemと一時的な資格情報cache

互換adapterの一覧と破棄順序は[Files.AppのCore統合アーキテクチャ](files-app.md)に記載しています。これは新しい `Files` の依存方向を
規定する文書ではありません。
