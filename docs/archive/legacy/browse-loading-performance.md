# Browse 読み込み性能と実行経路

この文書は、folder navigation の要求から最初の意味のある行が表示され、表示範囲の補助情報が揃うまでの実行経路、計測方法、性能上の不変条件を定義します。
CoreModel、項目 AppModel、Shell Session、ViewModel、View の所有関係は [Trickle-down MVVM と Files の設計規約](trickle-down-mvvm.md)に従います。

## 修正した回帰

### 空の Details 行

動的列への移行後、`DetailsFolderView` の `DataTemplate` から、テンプレート外側の `UserControl` を `ElementName=Root` で参照していました。
WinUI の `DataTemplate` は独立した名前スコープを作るため、この参照では `FolderBrowserViewModel.DetailsColumns` が `DetailsRow.Columns` へ届きません。
`DetailsRow.Rebuild()` は列がない場合に子要素を作らないので、`ListViewItem` は実体化されても表示内容が空でした。

列は Core や項目 ViewModel の状態ではなく、Details view の表示状態です。修正後は `FolderViewInteraction` が `ContainerContentChanging` で実体化された `DetailsRow` に現在の列を渡し、列変更時は実体化中の行だけを更新します。
ストレージ列挙や列値の取得を View へ移していないため、Trickle-down の依存方向は維持されます。

`DetailsFolderView` は `ItemsStackPanel` を明示して virtualization を維持し、`ShowsScrollingPlaceholders="False"` により高速スクロール中の意図的な空 placeholder を表示しません。

### 最初の行が全列挙を待っていた

Windows source は Shell STA から 32 項目ずつストリーミングしていましたが、`BrowseSession` がすべてを `List<IStorableModel>` へ蓄積し、全件を並べ替えた後でしか `ItemsChanged` を発行していませんでした。
後段の通知を 32 / 128 件に分割しても、最初の通知は全列挙後なので time to first row は改善しません。

修正後は最初の 32 項目を直ちに公開し、以後の batch を 256、512、最大 1,024 項目へ適応的に広げます。
列挙中は provider の順序で追記するため既存行の位置と item identity を保ち、列挙完了時に必要な場合だけ 1 回並べ替えます。
入力がすでに並べ替え済みなら `BrowseItemProjection` がそれを追跡し、最終 sort と reset を省略します。

最初の batch を公開した後も、以前の context、projection、selection、presentation snapshot は navigation の確定まで保持します。
列挙が失敗または cancel された場合は、公開済みの新しい項目を破棄して以前の状態を 1 回の reset で復元します。
新しい navigation は前の navigation token を cancel し、同じ navigation lock を通るので、古い世代の項目が後から確定することはありません。

### 先読み処理の増殖と直列待ち

以前の `BrowsePrefetchCoordinator` は viewport 更新ごとに `Task.Run` を作り、cancel を無視する provider があると完了していない task が増え続ける可能性がありました。
また、1 項目の property が完了してから thumbnail を開始していたため、遅い Shell property が最初の icon / thumbnail を不必要に遅らせていました。

修正後は capacity 1、`DropOldest` の bounded channel を property と thumbnail に 1 本ずつ持ちます。
各 lane は reader 1 つ、同時実行数 2 で、全体の provider 呼び出しは最大 4 です。新しい viewport は古い request を cancel して最新 request だけを残します。
2 lane は独立しているため、property が遅くても thumbnail は開始できます。結果は generation、content version、item instance を Core で再検証してから公開します。

## 実行マップ

| 段階 | 所有者 | 通常の実行スレッド | 頻度 | 最初の行を待たせるか |
| --- | --- | --- | --- | --- |
| command invocation | `WindowCommandManager` / navigation handler | UI | navigation ごと | はい。要求の入口だけ |
| tab / pane routing と cancel | `BrowsePaneSession` | 呼び出し元、await 後は任意 | navigation ごと | はい。前要求を cancel |
| location resolve | `IBrowseLocationResolver` / source | Core、Windows は ordered Shell STA | navigation ごと | はい |
| watcher start | `BrowseContextState` / `IFolderChangeSource` | source scheduler | navigation ごと | はい。列挙より先 |
| folder enumeration | `IBrowseLocationContext` | provider。Windows は ordered Shell STA で 32 件ずつ | 項目ごと / Shell batch ごと | 最初の 32 項目まで |
| CoreModel と項目 AppModel 作成 | storage source / model factory | provider thread | 項目ごと | 最初の batch の項目だけ |
| Core projection | `BrowseItemProjection` | enumeration continuation | Core batch ごと | 最初の 32 項目だけ |
| immutable change の投影 | `BrowsePresentationAdapter` | Core event thread | Core batch ごと | WinUI を待たない |
| dispatcher enqueue | `BrowsePresentationAdapter` | Core event thread | coalesced drain ごと | 1 回の enqueue まで |
| ViewModel collection 適用 | `FolderBrowserViewModel` | UI | 最大 128 項目 / drain、4 ms budget | 最初の UI batch だけ |
| collection notification | `BulkObservableCollection` | UI | contiguous range ごとに 1 回 | 最初の range だけ |
| container / template realization | `ListView` / `ItemsStackPanel` | UI | viewport 内の項目ごと | 最初の visible row まで |
| Details 列の接続 | `FolderViewInteraction` | UI | 実体化 row ごと | 安価な DP 設定だけ |
| primary text binding | `DetailsRow` / `BrowseItemViewModel` | UI | 実体化 cell ごと | name、kind、reference だけ |
| property prefetch | property lane | Core worker、Windows は Shell scheduler | viewport と look-ahead の項目だけ | いいえ |
| thumbnail prefetch | thumbnail lane | Core worker、Windows は concurrent Shell STA | viewport と look-ahead の項目だけ | いいえ |
| property dictionary 適用 | adapter / item ViewModel | UI、coalesced | 変更された表示項目ごと | いいえ |
| thumbnail decode と適用 | adapter / `ThumbnailImageFactory` | UI、decode gate 最大 2 | thumbnail ごと | いいえ |
| selection restoration | `BrowseSelectionModel` → adapter → View | Core の key 正規化後、UI drain | batch / navigation ごと | いいえ |
| final sort | `BrowseItemProjection` | enumeration continuation | navigation あたり最大 1 回 | いいえ。行はすでに表示済み |

Core event は UI thread で発生する保証がありません。adapter は immutable change と generation / version をコピーし、UI dispatcher の bounded drain でだけ WinUI collection、`BitmapImage`、item ViewModel を更新します。
thumbnail と extended property は初期 batch の公開経路に含まれません。

## 計測

Debug build で `FILES_DIAGNOSTIC_LOG=1` を設定すると、`Stopwatch.GetTimestamp()` と managed thread ID を含む次の milestone を Debug output へ記録します。
`Stopwatch` は monotonic timer で、経過時間の計測に wall clock を使いません。

- navigation requested、previous navigation cancelled、folder resolved
- enumeration started、first storage item returned、enumeration completed
- first batch published、initial sort completed
- first item ViewModel created、Core change projection、dispatcher enqueue
- UI drain ごとの item / property / thumbnail 数、drain 時間、最長 UI drain
- observable collection 適用時間
- first container、Details row template、first meaningful row、first viewport
- first property load、first thumbnail load、viewport lane completion
- first decoded thumbnail displayed
- navigation ごとの collection change と callback 時間
- adapter lifetime の item ViewModel、dispatcher enqueue、property notification、thumbnail display の合計

アプリレベルの入力遅延はこのログだけから推測せず、同じ fixture で Windows Performance Recorder、Visual Studio Diagnostic Tools、または同等の UI thread / input trace と突き合わせます。

## 再現可能な性能テスト

`Files.Benchmarks --browse-scenario` は in-memory provider が 32 項目の burst で CoreModel と項目 AppModel を作り、100、1,000、10,000、44,000 項目を各 5 回実行した中央値を JSON で出力します。
Shell、disk、network は含みません。測定値は navigation request から first item、first Core batch、全件確定までと、総割り当て、Core notification 数です。

同一の Release x64 build、同一プロセス条件で取得した値は次のとおりです。時間は ms、allocation は byte です。

| 項目数 | first batch 変更前 | first batch 変更後 | total 変更前 | total 変更後 | allocation 変更前 | allocation 変更後 | notification 変更前 → 変更後 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 0.2270 | 0.0815 | 0.2794 | 0.2566 | 87,600 | 87,808 | 2 → 2 |
| 1,000 | 1.8215 | 0.1109 | 2.4103 | 2.0980 | 886,328 | 872,464 | 9 → 4 |
| 10,000 | 17.8874 | 0.1190 | 42.1126 | 31.8275 | 11,741,744 | 9,026,720 | 79 → 12 |
| 44,000 | 148.3515 | 0.0883 | 193.8913 | 200.0770 | 98,220,704 | 45,774,664 | 345 → 46 |

44,000 項目では time to first batch が 99.94% 短く、allocation は約 53% 少なく、notification は約 87% 少なくなりました。
一方で synthetic provider の全件確定時間は約 3% 長く、この値を total throughput の改善とは扱いません。10,000 項目以下では total も短くなっています。
本変更の第一の効果は、全列挙を UI 表示の依存関係から外したことです。

通常の自動テストは wall-clock の厳しい閾値だけに依存せず、次の不変条件を検証します。

- 100 / 1,000 / 10,000 / 44,000 項目で最初の 32 項目が全列挙前に公開される。
- adaptive Core batch 数はそれぞれ 2 / 4 / 12 / 46 以下である。
- UI adapter の 1 drain は最大 128 項目で、44,000 項目でも 1 項目ごとの dispatcher enqueue を行わない。
- 行は primary text と列を持ち、property enrichment は同じ stable-key ViewModel を更新する。
- slow property が thumbnail lane を止めない。
- 100 回の viewport burst でも lane concurrency は property 2、thumbnail 2 を超えず、未完了 task が request 数に比例して増えない。
- partial enumeration の failure / cancellation は以前の context と項目を復元し、公開済みの新項目だけを破棄する。
- progressive loading 後の sort、rapid navigation、古い generation / model instance の結果拒否を検証する。

実行例:

```powershell
msbuild -restore tests/Files.UnitTests/Files.UnitTests.csproj -p:Configuration=Release -p:Platform=x64 -v:quiet -clp:ErrorsOnly
tests/Files.UnitTests/bin/x64/Release/net10.0-windows10.0.26100.0/Files.UnitTests.exe

msbuild -restore tests/Files.UITests/Files.UITests.csproj -p:Configuration=Release -p:Platform=x64 -v:quiet -clp:ErrorsOnly
tests/Files.UITests/bin/x64/Release/net10.0-windows10.0.26100.0/Files.UITests.exe

msbuild -restore tests/Files.Benchmarks/Files.Benchmarks.csproj -p:Configuration=Release -p:Platform=x64 -v:quiet -clp:ErrorsOnly
tests/Files.Benchmarks/bin/x64/Release/net10.0-windows10.0.26100.0/Files.Benchmarks.exe --browse-scenario
```

BenchmarkDotNet の `BrowsePipelineBenchmarks` は同じ 4 サイズについて throughput と allocation distribution を取得します。`--smoke` は dry job を実行します。

## 設計文書と実装の不一致

調査時には次の不一致がありました。

1. `windows-storage.md` は folder 全体を buffer しないと記載していましたが、source の後にある `BrowseSession` が全件を buffer していました。
2. `view-settings.md` と `files-app.md` は navigation 成功まで context と一覧を交換しない atomic model だけを記載し、progressive presentation と rollback の状態を表現していませんでした。
3. Details 列は View が所有するという規約に合っていましたが、DataTemplate の名前スコープを越える binding に依存していました。
4. viewport prefetch は UI adapter が所有するという境界に合っていましたが、request ごとの task ownership が bounded ではなく、property と thumbnail が不必要に直列でした。

本変更は Core の source / context / projection ownership、UI adapter の dispatcher ownership、View の realization ownershipを変えず、それぞれの境界内で修正しています。

## 外部資料

- [Optimize ListView and GridView](https://learn.microsoft.com/en-ca/windows/apps/develop/performance/optimize-gridview-and-listview): virtualization、template と binding cost、incremental data の判断。
- [ListView and GridView data optimization](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/listview-and-gridview-data-optimization): placeholder と段階的な data virtualization。
- [Data binding in depth](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-in-depth): compiled binding、binding source、DataTemplate の境界。
- [ItemsStackPanel](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsstackpanel?view=windows-app-sdk-1.8): ListView 用 virtualizing panel。
- [.NET channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels): bounded channel、full mode、single reader の producer-consumer contract。
- [`Stopwatch.IsHighResolution`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.ishighresolution): monotonic high-resolution counter の実装条件。
- [Trickle-down MVVM](https://dev.to/arlodotexe/excellent-architecture-trickle-down-mvvm-45jk) と [Strix Music](https://github.com/Arlodotexe/strix-music): model ownership と UI へ向かう依存関係。

## 残る制約

- 実際の first rendered pixel、input latency、layout / measure、GPU composition は in-process presentation test では測れません。実アプリの trace を別に取得する必要があります。
- provider の列挙順が requested sort と異なる場合、最初の行は早く安定して表示されますが、列挙完了時の 1 回の sort で位置が変わります。
- cancel token を無視して停止しない外部 provider 呼び出しは強制終了できません。bounded lane により増殖は防ぎますが、その lane の次の処理は呼び出しが戻るまで待ちます。
- 44,000 項目の synthetic total time は変更前より約 3% 長いため、今後は final projection snapshot と model disposal の CPU profile を継続します。
- グループ化プロパティがまだ到着していない表示項目は、一時的に `Unspecified` グループへ入り、bounded prefetch の結果が届くと低優先度で正しいグループへ移ります。
