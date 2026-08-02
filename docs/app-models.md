# アプリケーションモデルグラフ

`Files.Core.AppModels` は、Files プロセスの UI 非依存な状態グラフです。trickle-down MVVM の中間層にあたり、
ViewModel はこのモデルを適応し、モデルは参照状態を所有しますが WinUI を参照しません。

```mermaid
flowchart TB
    App["FilesApplicationModel"]
    Window["WindowModel"]
    Tab["TabModel"]
    Pane["PaneModel"]
    Browse["BrowseSessionModel"]
    Work["プレビュー + 先読み"]

    App --> Window
    Window --> Tab
    Tab --> Pane
    Pane --> Browse
    Pane --> Work
```

## 責務

| モデル | 所有するもの | 所有しないもの |
| --- | --- | --- |
| `FilesApplicationModel` | ウィンドウとアクティブウィンドウの識別情報 | WinUI `Application` やアクティブ化 |
| `WindowModel` | 順序付きタブとアクティブタブ | `Window`、`AppWindow`、タイトルバー |
| `TabModel` | 1～2 個のペイン、アクティブペイン、分割方向 | タブコントロールやドラッグ表示 |
| `PaneModel` | ナビゲーション履歴、参照セッション、プレビューモデル、先読みコーディネーター | `Frame`、リストコントロール、プレビュー HWND |
| `BrowseSessionModel` | 場所コンテキスト、項目モデル、投影、選択、表示値、ビュー設定 | observable collection や XAML オブジェクト |

モデル ID はプロセス内の相関 ID です。ストレージの識別情報はウィンドウ、タブ、ペインの ID ではなく、
`StorableReference` から得ます。

## 責務の確認

`BrowseSessionModel` は、1 つの参照可能なペインの状態をまとめる aggregate root です。公開面は、
ナビゲーションのトランザクション、アクティブな場所コンテキスト、不変の項目スナップショット、選択、表示状態を調整します。
ブラウザーに関係するすべての処理を詰め込む場所ではありません。

現在の責務の境界は次のとおりです。

| 関心事 | 所有者 |
| --- | --- |
| 戻る/進むの履歴とペインのライフタイム | `PaneModel` と `BrowseNavigationHistory` |
| 場所ごとのオープンと列挙 | `IBrowseLocationHandler` と `IBrowseLocationContext` |
| 項目の順序と細粒度のコレクション変更 | `BrowseItemProjection` |
| プロパティとサムネイルのスケジュール | `BrowsePrefetchCoordinator` |
| プレビューの選択とライフタイム | `BrowsePreviewModel` |
| 作成、名前変更、コピー、移動、削除 | `IStorageOperationService` |
| observable collection と dispatcher への適用 | Files のコレクションアダプター |
| コマンド、ダイアログ、表示状態 | Files |

最初の Files 導入スライスでは `IBrowseSessionModel` を安定させます。新しい処理が独自のライフタイム、キュー、整合性不変条件を持つ場合、
責務を直接追加せず、この aggregate の背後に内部コラボレーターを抽出します。候補はナビゲーション準備、フォルダー変更の調整、表示状態です。
ソースファイルを短くするだけの目的で抽出せず、最初の UI コンシューマーで独立してテストできる境界を確認してください。

次のものを `BrowseSessionModel` に入れてはいけません。WinUI 型、observable collection、ローカライズ文字列、コマンド実装、ダイアログ、
Shell 相互運用、ソース固有の分岐、プロセスサービスの検索です。

## Trickle-down MVVM の適用

モデルグラフと ViewModel グラフは、同じ所有階層と粒度を保ちます。

| 層 | 原則 | 禁止すること |
| --- | --- | --- |
| AppModel | 子モデルを作成・所有し、設定済みの依存関係を子へ渡す | WinUI 型、View の表示状態、サービスロケーター |
| ViewModel | 直接の AppModel を通知、コマンド binding、表示用コレクションへ適応する | `FilesCoreRuntime`、`IFilesDataRoot`、`IServiceProvider` の探索 |
| View / Control | DP で ViewModel を受け取り、template と code-behind で UI 挙動を所有する | Control 内部実装のための汎用 ViewModel、Core の直接呼び出し |

ViewModel が Core の差分を `ObservableCollection` へ投影する場合は、明示的な UI adapter を合成ルートで作ります。
adapter のために dispatcher や data root が必要でも、それを入れ子の ViewModel へ無制限に引き継がないでください。
現在の `RootViewModel`、`TabViewModel`、`PaneViewModel`、`FolderBrowserViewModel` に残るこの形は、最初の browsing slice の移行例外です。
新しい ViewModel は、直接モデル 1 つで構築できることを受け入れ条件にします。

View は次の境界を守ります。`PaneHost` は pane の配置と active 状態だけを扱い、`PaneView` が pane ごとのスクロールを所有します。
`PaneContentView` は content kind の template 選択、`FolderBrowser` は view mode の選択、各 folder view は入力・選択・viewport の UI 操作を所有します。

## グラフの作成

通常のアプリケーションは `FilesCoreRuntime` から準備済みの `FilesApplicationModel` を受け取ります。テストや特殊なホストでは、
`BrowsePaneFactory` からグラフを構築できます。

```csharp
await using var runtime = new FilesCoreBuilder()
	.AddWindowsStorage()
	.Build();

var window = await runtime.Application.CreateWindowAsync(
	HomeLocation.Instance,
	cancellationToken);
var pane = window.ActiveTab!.ActivePane!;
```

`CreateWindowAsync` は 1 つのタブと 1 つのペインを作ります。タブが持てるペインは最大 2 つです。

```csharp
var secondPane = await window.ActiveTab!.OpenSplitAsync(
	PaneSplitOrientation.Vertical,
	cancellationToken: cancellationToken);
```

子を閉じると、終了操作が完了する前にサブツリー全体を破棄します。

## ペインのナビゲーション

`PaneModel` はナビゲーションを直列化します。push が成功すると古い forward 分岐を削除し、replace は現在の履歴エントリを更新します。
戻ると進むでは、参照セッションが宛先を受け入れた後にだけ履歴カーソルを移動します。

```mermaid
sequenceDiagram
    participant VM as PaneViewModel
    participant Pane as PaneModel
    participant History as NavigationHistory
    participant Session as BrowseSession
    participant Resolver as LocationResolver

    VM->>Pane: NavigateAsync(location)
    Pane->>Session: NavigateAsync(location)
    Session->>Resolver: OpenAsync(location)
    Resolver-->>Session: 所有されるコンテキスト
    Session-->>Pane: 確定した場所
    Pane->>History: Push(location)
    Pane-->>VM: StateChanged
```

履歴にはパスではなく `BrowseLocation` の値を格納します。`FolderLocation` は安定した `StorableReference` を持ち、復旧アドレスを置き換えても重複エントリを作りません。
最新の場所の値が古いエントリを置き換えるため、`LastKnownAddress` は新鮮に保たれます。

`ArchiveLocation` は外側アーカイブの安定した参照と、正規化された論理エントリパスを含みます。スコープ付きアーカイブエントリのソース ID や認証情報を含めてはいけません。

`BrowseNavigationHistorySnapshot` は不変で、カーソルを検証します。多相な `BrowseLocation` の値を明示的なシリアライズスキーマへ変換すれば、
Files の永続化 DTO に適しています。ウィンドウセッションの保存とバージョン管理は、アプリケーションのアクティブ化とユーザー設定ポリシーを含むため Files に属します。

## 上位へのナビゲーション

`PaneModel.GoUpAsync` はまず、アーカイブのような論理的な場所について `IBrowseLocationParentResolver` を尊重します。アーカイブ内では別の `ArchiveLocation` を作り、
アーカイブのルートからは外側ストレージ項目の親を解決します。その他のフォルダーでは現在の `IFolderModel` を使い、親の安定した参照を取得して一時的な親モデルを作り、
ナビゲーション後に破棄します。ルートまたはフォルダーでない場所では `false` を返します。

```mermaid
flowchart TD
    Pane["現在のペイン"]
    Logical{"論理的な親リゾルバー?"}
    Folder{"フォルダーモデル?"}
    Parent["親モデルを解決"]
    Target["BrowseLocation"]
    Navigate["ナビゲートして push"]
    Stop["false を返す"]

    Pane --> Logical
    Logical -->|Yes| Target
    Logical -->|No| Folder
    Folder -->|No| Stop
    Folder -->|Yes| Parent
    Parent --> Target
    Target --> Navigate
```

## 選択と項目アクセス

選択はモデル参照やアドレスではなく、安定した `StorableKey` の値として `BrowseSessionModel` に保存します。
操作で現在のモデルスナップショットが必要な場合は、`GetFocusedItem()` と `GetSelectedItems()` を使います。
ナビゲーションやフォルダー変更イベントをまたいでスナップショットを保持せず、代わりに `StorableReference` の値を取得してください。

```csharp
var selectedReferences = pane.BrowseSession
	.GetSelectedItems()
	.Select(static item => item.Reference)
	.ToArray();
```

## ビューポート処理

ViewModel は `PaneModel.UpdateViewport` で表示されている項目範囲を報告します。ペインは `BrowsePrefetchCoordinator` に委譲し、範囲を限定したプロパティとサムネイルを要求し、
古い世代の結果を拒否します。

UI は実体化された要素ごとではなく、範囲が変わった後にこれを呼び出します。コーディネーターは表示範囲を優先し、置き換えられた処理をキャンセルします。

## イベントと UI dispatch

AppModel のイベントは、モデル遷移を確定したスレッドで発生します。WinUI dispatcher 上で実行される保証はありません。Files は次を行います。

1. イベントハンドラーで不変のモデルスナップショットを取得する。
2. ウィンドウ dispatcher に 1 回の更新をキューへ登録する。
3. ViewModel がまだ接続されていることを検証する。
4. 項目の変更を適用するか、observable projection をリセットする。

AppModel のイベント呼び出しは、購読者の例外を分離してトレースへ書き込みます。observer が確定済みのモデル遷移をロールバックしたり、他の observer を停止させたりすることはできません。

## キャンセルとライフタイム

すべての親が子を所有します。

```mermaid
flowchart TD
    Runtime["FilesCoreRuntime"]
    App["Application model"]
    Window["Window model"]
    Tab["Tab model"]
    Pane["Pane model"]
    Session["Session resources"]

    Runtime --> App
    App --> Window
    Window --> Tab
    Tab --> Pane
    Pane --> Session
```

破棄は冪等です。各モデルは次を行います。

- 新しい変更の受付を停止する。
- ライフタイムトークンをキャンセルする。
- 変更用セマフォを待つ。
- 子イベントの購読を解除する。
- 所有権と逆の順序で子を破棄する。
- 後続の子を放棄せず、クリーンアップ失敗を集約する。

Files はまず ViewModel とアクティブな Shell プレビューセッションを破棄し、その後 `FilesCoreRuntime` を破棄します。
UI スレッドからこれらの非同期破棄経路を同期的にブロックしてはいけません。
