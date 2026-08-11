# テストと性能

`Files.Core` には 3 つの検証レイヤーがあります。テストでは WinUI を避け、公開契約を通してモデルグラフを検証します。

## ユニットテスト

`tests/Files.UnitTests` では次を対象にします。

- 遅延項目機能の解決、合成、所有権、非同期破棄。
- サムネイルキャッシュの無効化競合と LRU 動作。
- ストリームプレビューの所有権、ブロック、キャンセル、サイズ制限。
- 参照ナビゲーション、置換、フォルダー変更の差分適用、選択、投影、プレビュー、ビューポート先読み。
- アプリケーション/ウィンドウ/タブ/ペインの所有権とナビゲーション履歴。
- 項目、項目機能、CoreModel の await 済み破棄とクリーンアップ失敗の集約。
- 未構築ビルダーのクリーンアップ、所有権移譲、構築失敗、ランタイム破棄。
- 操作のルーティング、enum 検証、結果/進行状況の不変条件。
- アーカイブパスの安全性、バックエンドのフォールバック、暗号化のルーティング、認証情報の再試行、論理的な親へのナビゲーション。
- FTP パスの正規化、ルート包含、アドレス解決、ストリーム/セッション所有権、プロパティ合成、同一ソースの変更。
- ストレージ識別情報と復旧アドレスの等値性。

決定論的なモデル動作にはテストダブルを使います。`IStorableModel` を作成したテストは、所有権をセッションへ移さない限り、そのモデルを所有します。

## Windows 統合テスト

Windows テストは実際の一時ファイルと Shell API を使い、次を検証します。

- 項目解決と安定したファイルシステム識別情報。
- フォルダー列挙とストリーム。
- サムネイル PNG 抽出。
- 型付きプロパティ抽出。
- Shell スケジューラーのアパートメントと同時実行の動作。
- `SHChangeNotifyRegister` のフォルダー通知。
- 作成、名前変更（大文字小文字だけの変更を含む）、コピー、移動、完全削除。
- コントローラーのダブルを使ったプレビュー関連付けとセッション調整。

プロセスレベルの Shell 動作を共有するテストには `DoNotParallelize` を付けます。各テストは固有の一時ディレクトリを作成し、`finally` で削除します。

Windows Shell プレビューコントローラーには別の手動スモーク境界があります。ホストされたテストマシンにインストールされた第三者ハンドラーやアウトオブプロセス COM サーバーは決定論的にできないため、
代表的なローカル `.txt`、`.pdf`、Office ファイルに対して `Files` のホストアダプターを実行します。

アーカイブシナリオテストでは、次の小さな fixture をコミットして保持します。暗号化なし/ありの ZIP と 7z、ヘッダー暗号化 7z、合成フォルダー、大文字小文字が異なる名前、
不正なトラバーサルエントリ、シーク不可の backing stream です。Shell の適用可否は OS バージョンではなく項目機能を基準に決まるため、各 fixture を Windows 10 と現在の Windows 11 イメージで実行します。
fixture のパスワードを本番テレメトリやエラーメッセージに含めてはいけません。

FTP 統合テストは公開エンドポイントではなく、分離された使い捨てサーバーで実行します。平文 FTP、明示的/暗黙的 TLS、認証失敗、MLST のないサーバー、UTF-8 とエスケープ済みの名前、
シーク不可ストリーム、再帰操作、キャンセル、サーバーのパス大文字小文字動作を対象にします。ユニットテストは `IFtpSession` のダブルを使い、ネットワークアクセスを必要としません。
FTP パスワードをテスト出力、アドレス、スナップショット、表示可能な CI 変数に含めてはいけません。

## ベンチマーク

`tests/Files.Benchmarks` は決定論的なアーキテクチャオーバーヘッドを測定します。

- ファクトリ数を変えた項目機能解決の cold/cached 動作。
- サムネイルキャッシュのヒット、ミス、挿入、追い出し。
- 100、1,000、10,000、44,000 項目の browse enumeration、AppModel 作成、adaptive projection batch、通知、allocation。

`tests/Files.UITests` は WinUI テストホストで、実際の `BrowseSession`、`BrowsePresentationAdapter`、手動 UI dispatcher を接続します。
最初の binding-ready 行、dispatcher / UI batch 数、1 callback の最大時間、Details 列の realization、stable row への progressive property 更新を検証します。
WindowsAppSDK の bootstrap と deployment manager 初期化はこの in-process test host では無効にし、WinUI object の実描画は UI / Axe test 境界に残します。

実行方法:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj `
	-c Release -- --filter '*'
```

dry smoke 設定:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj `
	-c Release -- --smoke
```

Shell、ディスク、ネットワーク、ソースの遅延をマイクロベンチマークへ混ぜてはいけません。それらはシナリオテストとして、マシンの詳細、ウォーム/コールドキャッシュ状態、項目数、
インストール済み Shell 拡張を記録して測定します。

## Files デバッグスモークと性能記録

最初の `Files` 垂直スライスは、Core 契約を通した手動デバッグスモークで確認します。x64 の Developer PowerShell で
`src/Files/Files.csproj`をDebugビルドし、次の操作を1回ずつ実行します。

1. 起動して`RootView`、custom tab strip、window単位の`NavigationToolbar`、native `NavigationView`、Homeの一覧が表示されることを確認する。
2. rooted Windows folderへ移動し、`ToolbarView`/`PaneHost`、folder/fileの表示、back/forward/up、refreshを確認する。
3. 複数選択を行い、folderをdouble-clickして`DetailsFolderView`のselectionとregistered `files.item.open` commandによるnavigationがCoreへ反映されることを確認する。
4. new tabを作成して各tabのfolder contentが独立していることを確認し、paneを追加/切り替え/閉じる。
5. ウィンドウを閉じ、Core runtimeの非同期破棄が完了することを確認する。

Visual StudioのDebug Diagnostic Toolsまたは同等の計測で、起動から最初の一覧表示、folder移動の開始から
一覧表示、refresh完了までを記録します。Debugの値は開発中の退行検出に使い、マシン間比較やリリース基準には使いません。
記録にはWindowsバージョン、CPU、項目数、cold/warm cache、Shell拡張の有無を含めます。

再現可能な比較値が必要になったら、同じfixtureと項目数でFiles.Core benchmarkをRelease実行し、Debugスモークの
startup/navigation/refresh観測値と混ぜずに保存します。`Files.Benchmarks` は UI 描画時間ではなく、Core の resolve、
enumeration、projection、selection reconciliationの境界を対象に追加します。

## CI

`.github/workflows/files-core-ci.yml` は Windows x64 で Core のテストをビルド・実行し、push 先が `new` のとき、該当する pull request と手動 dispatch でベンチマークの smoke job を実行します。

ローカルでコミットする前に:

```powershell
dotnet build tests/Files.UnitTests/Files.UnitTests.csproj `
	-c Release -p:Platform=x64
dotnet test tests/Files.UnitTests/Files.UnitTests.csproj `
	-c Release -p:Platform=x64 --no-build
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj `
	-c Release -p:Platform=x64 -- --smoke
git diff --check
```

Core プロジェクトは警告をエラーとして扱い、trimming/AOT 互換性分析を有効にします。Release ビルドなしのテスト成功だけでは十分ではありません。
