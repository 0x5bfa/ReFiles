# Files と Files.Core のアーキテクチャ

この文書群は、UI に依存しない `Files.Core` 基盤と、それを利用する `Files` WinUI ホストのアーキテクチャを定義します。
設計は [Trickle-down MVVM と Files の設計規約](trickle-down-mvvm.md) に従います。長寿命の依存関係は 1 回だけ合成してモデルグラフへ渡し、項目のオプション機能は項目機能単位で遅延合成します。

`Files.App` は旧アプリケーションを指す場合にだけ使います。新しい WinUI ホスト、View、ViewModel は `Files` と呼びます。

## システム境界

```mermaid
flowchart TB
    Views["WinUI ビュー"]
    ViewModels["Files ViewModel"]
    ShellSessions["Shell Session Model"]
    Workspace["StorageWorkspace"]
    AppModels["項目 AppModel"]
    ItemFeatures["項目機能"]
    CoreModels["OwlCore.Storage CoreModel"]
    Sources["ストレージ/プラットフォームソース"]

    Views --> ViewModels
    ViewModels --> ShellSessions
    ViewModels --> AppModels
    ShellSessions --> Workspace
    Workspace --> AppModels
    AppModels --> ItemFeatures
    AppModels --> CoreModels
    ItemFeatures --> Sources
    CoreModels --> Sources
```

WinUI に依存しないコードを最終的に 1 つの物理的な `Files.Core` プロジェクトへ統合する場合でも、論理的なレイヤーは分離したままにします。

## モデル用語

`Files.Core` はアセンブリ境界であり、単一のアーキテクチャレイヤーの名前ではありません。次の用語を一貫して使います。

| 用語 | 具体的な型 | 意味 |
| --- | --- | --- |
| ストレージ CoreModel | OwlCore.Storage `IStorable`、`IFile`、`IFolder` | ソースが扱う最小限のストレージ形状 |
| 項目 AppModel | `Files.Core.Models.IStorableModel` | Files の識別情報、ライフタイム、合成済み項目機能 |
| ストレージ Workspace | `Files.Core.Data.IStorageWorkspace` | ソースの列挙と、アドレスまたは安定参照から項目 AppModel を解決する UI 非依存ルート |
| Shell Session Model | `Files.Core.Sessions.*` と参照モデル | ウィンドウ、タブ、ペイン、ナビゲーションなど、復元可能なアプリケーションセッション状態 |
| ViewModel | `Files.ViewModels.*` | 対応する Session/AppModel と明示的な UI adapter を WinUI バインディングへ適応する薄いラッパー |

項目 AppModel と Shell Session Model はどちらも UI 非依存ですが、同じモデルレイヤーではありません。前者はストレージ CoreModel を Files 向けに適応し、
後者は UI ホストが持つセッション状態を表します。`Files.Core.Sessions` はそのための明示的な名前空間であり、CLI から項目 AppModel と同一視しません。

## 依存関係の規則

| レイヤー | 所有するもの | 依存できるもの |
| --- | --- | --- |
| Views | コントロール、表示状態、入力ルーティング | ウィンドウ単位の ViewModel |
| ViewModels | ローカライズ表示、コマンド binding、UI コレクション | 直接の Session/AppModel と明示的な UI adapter |
| Shell Sessions | ウィンドウ、タブ、ペイン、参照、選択、履歴 | Workspace、項目 AppModel、UI 非依存サービス |
| Storage Workspace / AppModels | ソース解決、安定参照、項目モデルとその所有権 | CoreModel と項目機能契約 |
| CoreModels | 標準化されたストレージ項目 | OwlCore.Storage とソース抽象化 |
| 項目機能 | サムネイル、プロパティ、プレビュー、ウォッチャーのオプション処理 | 項目コンテキストとソースサービス |
| ソース | Windows Shell、クラウド、FTP、アーカイブ | バックエンド/プラットフォーム API |

禁止する依存関係:

- `Files.Core` が WinUI、`Window`、`Frame`、`Page`、`DispatcherQueue` を参照すること。
- ViewModel が `IServiceProvider` や `Ioc.Default` をサービスロケーターとして使うこと。
- View が Windows Shell やストレージソースを直接呼び出すこと。
- ソースが ViewModel に依存すること。
- `IStorageSource` を `IStorable` のように扱うこと。
- `IItemFeatures` をプロセス全体の依存性注入として使うこと。
- パスや `LastKnownAddress` を項目識別情報として使うこと。

## Trickle-down による所有関係

```mermaid
flowchart TB
    Runtime["FilesCoreRuntime"]
    Workspace["IStorageWorkspace"]
    App["FilesApplicationSession<br/>(ShellSession)"]
    Window["WindowSession"]
    Tab["TabSession"]
    Pane["PaneSession"]
    Content["IPaneContentSession"]
    Item["IStorableModel"]

    Runtime --> Workspace
    Runtime --> App
    App --> Window
    Window --> Tab
    Tab --> Pane
    Pane --> Content
    Workspace --> Item
```

Storage Workspace と Shell Session は runtime が持つ別々のルートです。それぞれのグラフで親は子を所有し、非同期に破棄します。共有ソース、キャッシュ、スケジューラーはランタイムまたはソースレベルで所有します。
項目に結び付いたアダプターは、その項目の `ItemFeatures` が所有します。

## 文書一覧

新しい Files ホストを開始するときは、次の順に読んでください。

1. [Trickle-down MVVM の設計規約](trickle-down-mvvm.md)
2. [移行進捗](migration-progress.md)
3. [Shell Session モデルグラフ](app-models.md)
4. [合成ルート](composition.md)
5. [Files アーキテクチャ](files.md)
6. [Files.App の Core 統合（旧互換経路）](files-app.md)
7. [Files のコマンド実行](commands.md)
8. [Files の項目機能とアクティブ化](files-app-features.md)
9. [クリップボード、ドラッグ/ドロップ、Shell 連携](platform-interactions.md)
10. [テストと性能](testing.md)

参照文書:

- [ストレージモデルの境界と識別情報](storage-models.md)
- [アーカイブ参照と SevenZip フォールバック](archives.md)
- [FTP ストレージソース](ftp-storage.md)
- [項目機能の合成](item-features.md)
- [参照ビュー設定と投影](view-settings.md)
- [プレビューの流れと Shell セッション](previews.md)
- [ストレージ操作](operations.md)
- [`Files.Operations` によるクラッシュ耐性のある操作](server-operations.md)
- [Windows ストレージソース](windows-storage.md)
- [Windows Shell のスレッド処理](threading.md)
- [移行原則と物理プロジェクト統合](migration.md)

## 文書の役割

`migration-progress.md` だけが、完了した移行境界、現在の作業、次の移行単位を記録します。
その他の文書は、次のような概念を定義します。

- `trickle-down-mvvm.md`: CoreModel、AppModel、ViewModel、View の横断規約。
- `app-models.md`、`composition.md`、`files.md`: model graph、composition、UI ownership。
- `commands.md`、`platform-interactions.md`、`operations.md`: command、platform、operation contracts。
- `storage-models.md`、`windows-storage.md`、`ftp-storage.md`、`archives.md`: storage boundaries。
- `item-features.md`、`files-app-features.md`、`view-settings.md`、`previews.md`: item and presentation concepts。
- `threading.md`、`testing.md`、`server-operations.md`: threading、validation、failure-isolation concepts。

概念文書は進捗の一覧を複製せず、実装状況を参照するときは `migration-progress.md` を使用します。
