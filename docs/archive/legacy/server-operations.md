# `Files.Operations` によるクラッシュ耐性のある操作

保証するのは、`Files` がクラッシュしても、別プロセスの `Files.Operations` が実行中のファイル操作を継続することです。
サーバー、Windows、マシンの停止後に中断操作を自動再開しません。安全に再開できないコピーや移動を推測で再実行してはいけません。

1 項目の Core 契約は [ストレージ操作](operations.md) を参照してください。
class 内で本体を省略して `;` で終えている member は、追加するシグネチャを表します。

## 再考した構成

| 以前の案 | 採用する形 |
| --- | --- |
| サーバーにも完全な `FilesCoreRuntime` | 操作だけを持つ `StorageRuntime` |
| 永続 `OperationJob` とチェックポイント | メモリ内 `FileOperation` と再接続用 journal |
| 項目ごとの WinRT runtime class | WinRT value struct と一括配列 |
| 切断で失われる WinRT event | 単調増加する `Revision` と long polling |
| `OperationSync` + `OperationCenterModel` | アプリケーションスコープの `FileOperationsModel` |
| Files プロセス数でサーバー終了 | active operation + active call + idle timeout |

```text
Files
  CommandHandler -> FileOperationsModel -> IFileOperationClient
                                      -> WinRtFileOperationClient
                                      -> FileOperationServer

Files.Operations
  FileOperationServer -> FileOperationHost -> FileOperation
                                          -> OperationJournal
                                          -> StorageRuntime.Operations

Files.Core
  File operation values
  StorageRuntime
  IStorageOperationService
  WindowsStorageOperationHandler
```

## Files.Core の操作値

Core は WinRT を知らない通常の C# 型を持ちます。`Files` と `Files.Operations` の内部処理はこの型だけを使います。

```csharp
namespace Files.Core.Storage.FileOperations;

public static class FileOperationLimits
{
	public const int MaxItems = 4096;
	public const int MaxNameLength = 255;
	public const int MaxErrorDetailLength = 2048;
}

public enum FileOperationKind
{
	Create,
	Rename,
	Copy,
	Move,
	Delete,
}

public enum FileOperationState
{
	Queued,
	Running,
	Cancelling,
	Succeeded,
	CompletedWithErrors,
	Failed,
	Cancelled,
	Unknown,
}

public enum FileOperationItemState
{
	Queued,
	Running,
	Succeeded,
	Failed,
	Cancelled,
	Unknown,
}

public enum FileOperationErrorCode
{
	None,
	InvalidRequest,
	NotFound,
	AccessDenied,
	NameConflict,
	SourceUnavailable,
	NotSupported,
	Cancelled,
	ServerInterrupted,
	Unknown,
}
```

```csharp
public sealed record FileOperationReference(
	string SourceId,
	string ItemId,
	string? AddressScheme = null,
	string? AddressValue = null)
{
	public StorableReference ToReference()
	{
		var address = AddressScheme is not null
			&& AddressValue is not null
				? new StorageAddress(AddressScheme, AddressValue)
				: null;

		return new StorableReference(
			new StorageSourceId(SourceId),
			ItemId,
			address);
	}

	public static FileOperationReference FromReference(
		StorableReference reference) =>
		new(
			reference.SourceId.Value,
			reference.ItemId,
			reference.LastKnownAddress?.Scheme,
			reference.LastKnownAddress?.Value);
}

public sealed record FileOperationRequest(
	string OperationId,
	FileOperationKind Kind,
	ImmutableArray<FileOperationReference> Items,
	FileOperationReference? DestinationFolder,
	string? Name,
	StorageItemKind? CreatedItemKind,
	StorageConflictBehavior ConflictBehavior,
	bool Permanently);
```

一覧取得では軽い summary、詳細画面では項目結果を含む snapshot を使います。

```csharp
public sealed record FileOperationItemSnapshot(
	int Index,
	FileOperationReference Input,
	FileOperationItemState State,
	FileOperationReference? Result,
	FileOperationErrorCode ErrorCode,
	string? ErrorDetail);

public sealed record FileOperationSummary(
	string OperationId,
	FileOperationKind Kind,
	FileOperationState State,
	int CompletedItems,
	int FailedItems,
	int TotalItems,
	FileOperationReference? CurrentItem,
	FileOperationErrorCode ErrorCode,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt);

public sealed record FileOperationSnapshot(
	FileOperationSummary Summary,
	ImmutableArray<FileOperationItemSnapshot> Items);

public sealed record FileOperationList(
	long Revision,
	ImmutableArray<FileOperationSummary> Operations);
```

## 操作専用 `StorageRuntime`

`Files.Operations` はウィンドウ、AppModel、項目機能、サムネイル、プレビュー、アーカイブを構築しません。

```csharp
namespace Files.Core.Storage.Runtime;

public sealed class StorageRuntime : IAsyncDisposable
{
	private readonly IReadOnlyList<IAsyncDisposable> ownedServices;

	internal StorageRuntime(
		IStorageOperationService operations,
		IReadOnlyList<IAsyncDisposable> ownedServices)
	{
		Operations = operations;
		this.ownedServices = ownedServices;
	}

	public IStorageOperationService Operations { get; }

	public async ValueTask DisposeAsync()
	{
		foreach (var service in ownedServices.Reverse())
		{
			await service.DisposeAsync().ConfigureAwait(false);
		}
	}
}

public sealed class StorageRuntimeBuilder : IAsyncDisposable
{
	public StorageRuntimeBuilder AddHandler(
		IStorageOperationHandler handler);

	internal void Own(IAsyncDisposable service);

	public StorageRuntime Build();

	public ValueTask DisposeAsync();
}

public static class WindowsStorageRuntimeBuilderExtensions
{
	public static StorageRuntimeBuilder AddWindowsOperations(
		this StorageRuntimeBuilder builder,
		WindowsStorageSource? source = null)
	{
		var windowsSource = source ?? new WindowsStorageSource();
		builder.AddHandler(
			new WindowsStorageOperationHandler(windowsSource));

		if (source is null)
		{
			builder.Own(windowsSource);
		}

		return builder;
	}
}
```

```csharp
await using var storage = new StorageRuntimeBuilder()
	.AddWindowsOperations()
	.Build();

var operations = storage.Operations;
```

`FilesCoreBuilder.AddWindowsStorage()` はブラウザー用です。操作サーバーでは使いません。

## WinRT ABI

ライブ IPC に JSON は使いません。入力と出力は値として marshal できる WinRT struct にし、項目は配列で一括転送します。
struct に配列や nullable を入れず、nullable は `HasDestination` などの flag で表します。

```csharp
namespace Files.Operations;

public enum OperationKindData
{
	Create,
	Rename,
	Copy,
	Move,
	Delete,
}

public enum OperationStateData
{
	Queued,
	Running,
	Cancelling,
	Succeeded,
	CompletedWithErrors,
	Failed,
	Cancelled,
	Unknown,
}

public enum OperationItemStateData
{
	Queued,
	Running,
	Succeeded,
	Failed,
	Cancelled,
	Unknown,
}

public enum OperationErrorCodeData
{
	None,
	InvalidRequest,
	NotFound,
	AccessDenied,
	NameConflict,
	SourceUnavailable,
	NotSupported,
	Cancelled,
	ServerInterrupted,
	Unknown,
}

public enum OperationItemKindData
{
	None,
	File,
	Folder,
}

public enum OperationConflictBehaviorData
{
	Fail,
	GenerateUniqueName,
}
```

```csharp
public struct OperationReferenceData
{
	public string SourceId;
	public string ItemId;
	public string AddressScheme;
	public string AddressValue;
}

public struct OperationRequestData
{
	public string OperationId;
	public OperationKindData Kind;
	public bool HasDestination;
	public OperationReferenceData Destination;
	public string Name;
	public OperationItemKindData CreatedItemKind;
	public OperationConflictBehaviorData ConflictBehavior;
	public bool Permanently;
}

public struct OperationSummaryData
{
	public string OperationId;
	public OperationKindData Kind;
	public OperationStateData State;
	public int CompletedItems;
	public int FailedItems;
	public int TotalItems;
	public bool HasCurrentItem;
	public OperationReferenceData CurrentItem;
	public OperationErrorCodeData ErrorCode;
	public long CreatedAtUnixMilliseconds;
	public long UpdatedAtUnixMilliseconds;
}

public struct OperationItemData
{
	public int Index;
	public OperationReferenceData Input;
	public OperationItemStateData State;
	public bool HasResult;
	public OperationReferenceData Result;
	public OperationErrorCodeData ErrorCode;
	public string ErrorDetail;
}
```

一覧の `Revision` と内容、snapshot の summary と項目を同じ時点で固定するため、結果だけ immutable runtime class にします。
項目ごとの runtime class は作りません。結果 class は server 内部でだけ生成し、返す配列は copy します。

```csharp
public sealed class OperationListResult
{
	private readonly OperationSummaryData[] operations;

	internal OperationListResult(
		long revision,
		OperationSummaryData[] operations)
	{
		Revision = revision;
		this.operations = operations.ToArray();
	}

	public long Revision { get; }

	public OperationSummaryData[] GetOperations() =>
		operations.ToArray();
}

public sealed class OperationSnapshotResult
{
	private readonly OperationItemData[] items;

	internal OperationSnapshotResult(
		OperationSummaryData summary,
		OperationItemData[] items)
	{
		Summary = summary;
		this.items = items.ToArray();
	}

	public OperationSummaryData Summary { get; }

	public OperationItemData[] GetItems() =>
		items.ToArray();
}
```

```csharp
public sealed class FileOperationServer
{
	public IAsyncOperation<OperationSnapshotResult> StartAsync(
		OperationRequestData request,
		[ReadOnlyArray] OperationReferenceData[] items);

	public IAsyncOperation<OperationSnapshotResult> GetAsync(
		string operationId);

	public IAsyncOperation<OperationListResult> ListAsync();

	public IAsyncOperation<OperationListResult> WaitForChangeAsync(
		long knownRevision);

	public IAsyncAction CancelAsync(string operationId);

	public IAsyncAction ForgetAsync(string operationId);
}
```

`ReadOnlyArray` は `System.Runtime.InteropServices.WindowsRuntime` の属性です。WinRT struct は public field だけを持ち、配列は method parameter または戻り値にだけ置きます。
これは `.winmd` がコンパイル時に検査する ABI です。App と Server は同じ package で更新するため、ライブ IPC 用の独立した JSON schema/version は不要です。

実装は薄い ABI adapter です。

```csharp
internal static class OperationDataMapper
{
	public static FileOperationRequest FromData(
		OperationRequestData request,
		IReadOnlyList<OperationReferenceData> items);

	public static OperationSnapshotResult ToResult(
		FileOperationSnapshot snapshot);

	public static OperationListResult ToResult(
		FileOperationList list);
}

public IAsyncOperation<OperationSnapshotResult> StartAsync(
	OperationRequestData request,
	[ReadOnlyArray] OperationReferenceData[] items)
{
	return AsyncInfo.Run(async _ =>
	{
		using var call = ServerProcess.Current.Lifetime.EnterCall();
		var coreRequest = OperationDataMapper.FromData(
			request,
			items);
		var snapshot = await ServerProcess.Current.Operations.StartAsync(
			coreRequest,
			ServerProcess.Current.ShutdownToken);
		return OperationDataMapper.ToResult(snapshot);
	});
}

public IAsyncOperation<OperationListResult> WaitForChangeAsync(
	long knownRevision)
{
	return AsyncInfo.Run(async cancellationToken =>
	{
		using var call = ServerProcess.Current.Lifetime.EnterCall();
		var list = await ServerProcess.Current.Operations
			.WaitForChangeAsync(
				knownRevision,
				cancellationToken);
		return OperationDataMapper.ToResult(list);
	});
}
```

`StartAsync` は client token を操作 token に使いません。受理後の処理は server-owned token で継続します。
`WaitForChangeAsync` のキャンセルは待機だけを止めます。

## サーバー合成ルート

WinRT activation は constructor injection を使えないため、`ServerProcess.Current` だけを明示的な process root とします。

```csharp
internal sealed class ServerProcess : IAsyncDisposable
{
	private static ServerProcess? current;
	private readonly CancellationTokenSource shutdown = new();
	private readonly StorageRuntime storage;

	public static ServerProcess Current =>
		current ?? throw new InvalidOperationException(
			"The server process has not been initialized.");

	public FileOperationHost Operations { get; }

	public ServerLifetime Lifetime { get; }

	public CancellationToken ShutdownToken => shutdown.Token;

	public static async Task<ServerProcess> CreateAsync(
		string dataPath,
		CancellationToken cancellationToken);

	public static void SetCurrent(ServerProcess process);

	public async ValueTask DisposeAsync()
	{
		shutdown.Cancel();
		await Operations.DisposeAsync();
		await storage.DisposeAsync();
		shutdown.Dispose();
		current = null;
	}
}
```

```csharp
static async Task Main()
{
	using var shutdown = new CancellationTokenSource();
	var dataPath = ApplicationData.Current.LocalFolder.Path;

	await using var process = await ServerProcess.CreateAsync(
		dataPath,
		shutdown.Token);
	ServerProcess.SetCurrent(process);

	using var registration = RegisterActivationFactories(
		[typeof(FileOperationServer)]);

	AppDomain.CurrentDomain.ProcessExit +=
		(_, _) => shutdown.Cancel();

	await process.Lifetime.WaitForExitAsync(shutdown.Token);
}
```

現在の public sealed class 全走査を explicit allowlist に置き換えます。
activation factory を登録するのは public constructor を持つ `FileOperationServer` だけです。server が返す `OperationListResult` と `OperationSnapshotResult` は activatable class ではありません。
`AppInstanceMonitor` と `Files/Program.cs` の server kill は削除します。

```xml
<OutOfProcessServer
	ServerName="Files.Operations"
	uap5:IdentityType="activateAsPackage"
	uap5:RunFullTrust="true">
	<Path>Files.Operations\Files.Operations.exe</Path>
	<Instancing>singleInstance</Instancing>
	<ActivatableClass
		ActivatableClassId="Files.Operations.FileOperationServer" />
</OutOfProcessServer>
```

## request から Core request への変換

1 回のユーザー操作を、順序付きの Core request へ展開します。

```csharp
internal sealed record FileOperationStep(
	FileOperationReference Input,
	StorageOperationRequest Request);

internal sealed record FileOperationPlan(
	string OperationId,
	string RequestHash,
	FileOperationKind Kind,
	ImmutableArray<FileOperationStep> Steps);

internal static class FileOperationRequestReader
{
	public static FileOperationPlan Read(
		FileOperationRequest request);
}
```

```csharp
var steps = request.Kind switch
{
	FileOperationKind.Create =>
	[
		new FileOperationStep(
			request.DestinationFolder!,
			new CreateItemOperationRequest(
				destination!,
				request.Name!,
				request.CreatedItemKind!.Value,
				request.ConflictBehavior)),
	],

	FileOperationKind.Rename =>
	[
		new FileOperationStep(
			request.Items[0],
			new RenameOperationRequest(
				items[0],
				request.Name!)),
	],

	FileOperationKind.Copy =>
		items.Select((item, index) =>
			new FileOperationStep(
				request.Items[index],
				new CopyOperationRequest(
					item,
					destination!,
					request.Name,
					request.ConflictBehavior)))
			.ToImmutableArray(),

	FileOperationKind.Move =>
		items.Select((item, index) =>
			new FileOperationStep(
				request.Items[index],
				new MoveOperationRequest(
					item,
					destination!,
					request.Name,
					request.ConflictBehavior)))
			.ToImmutableArray(),

	FileOperationKind.Delete =>
		items.Select((item, index) =>
			new FileOperationStep(
				request.Items[index],
				new DeleteOperationRequest(
					item,
					request.Permanently)))
			.ToImmutableArray(),

	_ => throw new InvalidDataException(
		"Unknown operation kind."),
};
```

`FileOperationRequestReader.Read` は Core request を作る前に次を検証します。

```csharp
Guid.TryParseExact(request.OperationId, "N", out _)
request.Items.Length <= FileOperationLimits.MaxItems
request.Name?.Length <= FileOperationLimits.MaxNameLength

Create: Items.Length == 0 && DestinationFolder != null
Rename: Items.Length == 1 && DestinationFolder == null
Copy:   Items.Length >= 1 && DestinationFolder != null
Move:   Items.Length >= 1 && DestinationFolder != null
Delete: Items.Length >= 1 && DestinationFolder == null

Name != null for Create/Rename
Name == null || Items.Length == 1 for Copy/Move
```

意味のない余分な field も拒否します。request hash は操作種別、`SourceId`、`ItemId`、宛先、名前、競合動作、完全削除を canonical order で hash します。
`LastKnownAddress` は識別情報ではないため hash に含めません。

```csharp
internal static class FileOperationRequestHasher
{
	public static string Hash(FileOperationRequest request);
}
```

## `FileOperationHost`

`FileOperationHost` はサーバープロセスに 1 つです。dictionary の値を WinRT 境界へ返さず、不変 snapshot を返します。

```csharp
internal sealed class FileOperationHost : IAsyncDisposable
{
	private readonly Dictionary<string, Entry> entries =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim stateGate = new(1, 1);
	private readonly SemaphoreSlim windowsExecutionGate =
		new(1, 1);
	private readonly IStorageOperationService operations;
	private readonly IOperationJournal journal;
	private readonly RevisionSignal changes;
	private readonly ServerLifetime lifetime;

	public static Task<FileOperationHost> CreateAsync(
		IStorageOperationService operations,
		IOperationJournal journal,
		ServerLifetime lifetime,
		CancellationToken cancellationToken);

	public Task<FileOperationSnapshot> StartAsync(
		FileOperationRequest request,
		CancellationToken serverToken);

	public Task<FileOperationSnapshot> GetAsync(
		string operationId,
		CancellationToken cancellationToken);

	public Task<FileOperationList> ListAsync(
		CancellationToken cancellationToken);

	public Task<FileOperationList> WaitForChangeAsync(
		long knownRevision,
		CancellationToken cancellationToken);

	public Task CancelAsync(
		string operationId,
		CancellationToken cancellationToken);

	public Task ForgetAsync(
		string operationId,
		CancellationToken cancellationToken);

	public ValueTask DisposeAsync();

	private sealed class Entry
	{
		public required string RequestHash { get; init; }
		public required FileOperationSnapshot Snapshot { get; set; }
		public FileOperation? ActiveOperation { get; set; }
	}
}
```

`StartAsync` の順序を変えてはいけません。

```csharp
var plan = FileOperationRequestReader.Read(request);

await stateGate.WaitAsync(serverToken);
try
{
	if (entries.TryGetValue(plan.OperationId, out var existing))
	{
		if (existing.RequestHash != plan.RequestHash)
		{
			throw new InvalidDataException(
				"OperationId is already used.");
		}

		return existing.Snapshot;
	}

	var operation = new FileOperation(
		plan,
		operations,
		windowsExecutionGate,
		OnOperationChangedAsync,
		serverToken);

	await journal.WriteAsync(
		OperationJournalEntry.Create(
			plan.RequestHash,
			operation.Snapshot),
		serverToken);

	entries.Add(
		plan.OperationId,
		new Entry
		{
			RequestHash = plan.RequestHash,
			Snapshot = operation.Snapshot,
			ActiveOperation = operation,
		});
	PublishState();
	operation.Start();
	return operation.Snapshot;
}
finally
{
	stateGate.Release();
}
```

同じ ID と同じ hash は既存 snapshot を返します。異なる hash は拒否します。
journal へ `Queued` を書く前に副作用を開始しません。

```csharp
private async ValueTask OnOperationChangedAsync(
	FileOperation operation,
	FileOperationSnapshot snapshot,
	bool isTerminal)
{
	await stateGate.WaitAsync();
	try
	{
		var entry = entries[snapshot.Summary.OperationId];
		if (!ReferenceEquals(entry.ActiveOperation, operation))
		{
			return;
		}

		entry.Snapshot = snapshot;
		if (isTerminal)
		{
			entry.ActiveOperation = null;
			await journal.WriteAsync(
				OperationJournalEntry.Create(
					entry.RequestHash,
					snapshot),
				CancellationToken.None);
		}

		changes.Pulse();
		lifetime.SetActiveOperationCount(
			entries.Values.Count(
				static value =>
					value.ActiveOperation is not null));
	}
	finally
	{
		stateGate.Release();
	}
}
```

`ForgetAsync` は terminal 状態だけを削除できます。`CancelAsync` は `FileOperation` の token だけを signal し、client call の token を保存しません。

## `FileOperation`

`FileOperation` は server-owned cancellation と 1 logical operation の状態を所有します。

```csharp
internal sealed class FileOperation
{
	private readonly FileOperationPlan plan;
	private readonly IStorageOperationService operations;
	private readonly SemaphoreSlim executionGate;
	private readonly SemaphoreSlim snapshotGate = new(1, 1);
	private readonly CancellationTokenSource cancellation;
	private readonly Func<
		FileOperation,
		FileOperationSnapshot,
		bool,
		ValueTask> publish;
	private Task? execution;

	public FileOperationSnapshot Snapshot { get; private set; }

	public Task Completion =>
		execution ?? Task.CompletedTask;

	public void Start() =>
		execution = RunAsync();

	public async ValueTask CancelAsync()
	{
		await UpdateAsync(
			snapshot =>
				FileOperationSnapshots.RequestCancellation(
					snapshot),
			isTerminal: false);
		cancellation.Cancel();
	}

	private async Task RunAsync()
	{
		var ownsGate = false;
		try
		{
			await executionGate.WaitAsync(cancellation.Token);
			ownsGate = true;
			await UpdateAsync(
				FileOperationSnapshots.Start,
				isTerminal: false);

			for (var index = 0;
				index < plan.Steps.Length;
				index++)
			{
				cancellation.Token.ThrowIfCancellationRequested();
				await UpdateAsync(
					value => FileOperationSnapshots.StartItem(
						value,
						index),
					isTerminal: false);

				var result = await operations.ExecuteAsync(
					plan.Steps[index].Request,
					cancellationToken: cancellation.Token);

				await UpdateAsync(
					value => FileOperationSnapshots.ApplyResult(
						value,
						index,
						result),
					isTerminal: false);
			}

			await UpdateAsync(
				FileOperationSnapshots.Complete,
				isTerminal: true);
		}
		catch (OperationCanceledException)
			when (cancellation.IsCancellationRequested)
		{
			await UpdateAsync(
				FileOperationSnapshots.Cancel,
				isTerminal: true);
		}
		catch (Exception error)
		{
			await UpdateAsync(
				value => FileOperationSnapshots.Fail(
					value,
					error),
				isTerminal: true);
		}
		finally
		{
			if (ownsGate)
			{
				executionGate.Release();
			}

			cancellation.Dispose();
		}
	}

	private async ValueTask UpdateAsync(
		Func<FileOperationSnapshot, FileOperationSnapshot> update,
		bool isTerminal)
	{
		await snapshotGate.WaitAsync();
		try
		{
			Snapshot = update(Snapshot);
			await publish(this, Snapshot, isTerminal);
		}
		finally
		{
			snapshotGate.Release();
		}
	}
}
```

snapshot の更新規則は 1 class に閉じ込めます。

```csharp
internal static class FileOperationSnapshots
{
	public static FileOperationSnapshot CreateQueued(
		FileOperationPlan plan);

	public static FileOperationSnapshot Start(
		FileOperationSnapshot snapshot);

	public static FileOperationSnapshot RequestCancellation(
		FileOperationSnapshot snapshot);

	public static FileOperationSnapshot StartItem(
		FileOperationSnapshot snapshot,
		int index);

	public static FileOperationSnapshot ApplyResult(
		FileOperationSnapshot snapshot,
		int index,
		StorageOperationResult result);

	public static FileOperationSnapshot Complete(
		FileOperationSnapshot snapshot);

	public static FileOperationSnapshot Cancel(
		FileOperationSnapshot snapshot);

	public static FileOperationSnapshot Fail(
		FileOperationSnapshot snapshot,
		Exception error);

	public static FileOperationSnapshot MarkUnknown(
		FileOperationSnapshot snapshot);
}
```

- 成功済み/失敗済みの項目結果を後のキャンセルで消さない。
- `Succeeded` は全項目成功、`CompletedWithErrors` は部分成功、`Failed` は成功なし。
- Shell が変更を確定した項目は、キャンセル要求後でも `Succeeded`。
- UI は `ErrorCode` をローカライズし、`ErrorDetail` を条件判定に使わない。

## revision、journal、サーバーライフタイム

```csharp
internal sealed class RevisionSignal
{
	public long Current { get; }

	public void Pulse();

	public Task<long> WaitAsync(
		long knownRevision,
		TimeSpan timeout,
		CancellationToken cancellationToken);
}
```

`Pulse` は revision を増やし、待機中の `TaskCompletionSource<long>` を完了させます。
`WaitForChangeAsync` は変更または 20 秒の timeout 後に完全な summary list を返します。

```csharp
internal static class OperationJournalSchema
{
	public const int Current = 2;
}

internal sealed record OperationJournalEntry(
	int SchemaVersion,
	string RequestHash,
	FileOperationSnapshot Snapshot)
{
	public static OperationJournalEntry Create(
		string requestHash,
		FileOperationSnapshot snapshot) =>
		new(
			OperationJournalSchema.Current,
			requestHash,
			snapshot);
}

internal interface IOperationJournal
{
	ValueTask<IReadOnlyList<OperationJournalEntry>> ReadAllAsync(
		CancellationToken cancellationToken);

	ValueTask WriteAsync(
		OperationJournalEntry entry,
		CancellationToken cancellationToken);

	ValueTask DeleteAsync(
		string operationId,
		CancellationToken cancellationToken);
}

internal sealed class JsonOperationJournal
	: IOperationJournal
{
	public JsonOperationJournal(string rootPath);

	public ValueTask<IReadOnlyList<OperationJournalEntry>>
		ReadAllAsync(CancellationToken cancellationToken);

	public ValueTask WriteAsync(
		OperationJournalEntry entry,
		CancellationToken cancellationToken);

	public ValueTask DeleteAsync(
		string operationId,
		CancellationToken cancellationToken);
}

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	WriteIndented = false)]
[JsonSerializable(typeof(OperationJournalEntry))]
internal sealed partial class OperationJournalJsonContext
	: JsonSerializerContext;
```

JSON を使うのはこの disk journal だけです。保存先は `operations/v2/{operationId}.json` とし、一時ファイルへ書いて同じ volume 上で atomic replace します。
schema version は古い journal を明示的に移行または拒否するために必要であり、ライブ IPC の互換性には使いません。
書くのは受理時と terminal 状態だけです。実行 plan、資格情報、PIDL、stream、token は保存しません。

```csharp
private static FileOperationSnapshot Recover(
	FileOperationSnapshot snapshot) =>
	snapshot.Summary.State is
		FileOperationState.Succeeded
		or FileOperationState.CompletedWithErrors
		or FileOperationState.Failed
		or FileOperationState.Cancelled
		or FileOperationState.Unknown
			? snapshot
			: FileOperationSnapshots.MarkUnknown(snapshot);
```

`Unknown` を同じ ID で再送しても再実行しません。再試行には新しい `OperationId` が必要です。

```csharp
internal sealed class ServerLifetime
{
	public ServerLifetime(TimeSpan idleDelay);

	public IDisposable EnterCall();

	public void SetActiveOperationCount(int count);

	public Task WaitForExitAsync(
		CancellationToken cancellationToken);
}
```

終了条件:

```csharp
activeCalls == 0
	&& activeOperations == 0
	&& idleGenerationIsStillCurrent
```

新しい call または operation は idle timer の generation を無効化します。

## Files client と model

ViewModel は生成された WinRT class を直接保持しません。

```csharp
public interface IFileOperationClient : IAsyncDisposable
{
	ValueTask<FileOperationSnapshot> StartAsync(
		FileOperationRequest request,
		CancellationToken cancellationToken);

	ValueTask<FileOperationSnapshot> GetAsync(
		string operationId,
		CancellationToken cancellationToken);

	ValueTask<FileOperationList> ListAsync(
		CancellationToken cancellationToken);

	IAsyncEnumerable<FileOperationList> WatchAsync(
		long knownRevision,
		CancellationToken cancellationToken);

	ValueTask CancelAsync(
		string operationId,
		CancellationToken cancellationToken);

	ValueTask ForgetAsync(
		string operationId,
		CancellationToken cancellationToken);
}
```

```csharp
internal sealed class WinRtFileOperationClient
	: IFileOperationClient
{
	private Server.FileOperationServer server = new();

	public async ValueTask<FileOperationSnapshot> StartAsync(
		FileOperationRequest request,
		CancellationToken cancellationToken)
	{
		var result = await server
			.StartAsync(
				OperationDataMapper.ToData(request),
				request.Items
					.Select(OperationDataMapper.ToData)
					.ToArray())
			.AsTask(cancellationToken);
		return OperationDataMapper.FromResult(result);
	}

	public async IAsyncEnumerable<FileOperationList> WatchAsync(
		long knownRevision,
		[EnumeratorCancellation]
		CancellationToken cancellationToken)
	{
		var revision = knownRevision;
		while (!cancellationToken.IsCancellationRequested)
		{
			var result = await server
				.WaitForChangeAsync(revision)
				.AsTask(cancellationToken);
			var list = OperationDataMapper.FromResult(result);
			revision = list.Revision;
			yield return list;
		}
	}
}
```

```csharp
internal static class OperationDataMapper
{
	public static OperationRequestData ToData(
		FileOperationRequest request);

	public static OperationReferenceData ToData(
		FileOperationReference reference);

	public static FileOperationSnapshot FromResult(
		OperationSnapshotResult result);

	public static FileOperationList FromResult(
		OperationListResult result);
}
```

mapper は ABI の empty string/flag と Core の nullable、Unix milliseconds と `DateTimeOffset` の変換を一か所で行います。
実装では server disconnect を分類し、`new FileOperationServer()`、`ListAsync()`、上限付き backoff の順で再接続します。

```csharp
public sealed class FileOperationsModel : IAsyncDisposable
{
	private readonly IFileOperationClient client;
	private readonly CancellationTokenSource lifetime = new();
	private ImmutableDictionary<string, FileOperationSummary> items =
		ImmutableDictionary<string, FileOperationSummary>.Empty
			.WithComparers(StringComparer.Ordinal);
	private Task? watchTask;
	private long revision;

	public event EventHandler? Changed;

	public ImmutableArray<FileOperationSummary> Items { get; }

	public async Task StartAsync(
		CancellationToken cancellationToken)
	{
		Apply(await client.ListAsync(cancellationToken));
		watchTask ??= WatchAsync(lifetime.Token);
	}

	public async ValueTask<FileOperationSummary> SubmitAsync(
		FileOperationRequest request,
		CancellationToken cancellationToken)
	{
		var snapshot = await client.StartAsync(
			request,
			cancellationToken);
		Upsert(snapshot.Summary);
		return snapshot.Summary;
	}

	public ValueTask CancelAsync(
		string operationId,
		CancellationToken cancellationToken) =>
		client.CancelAsync(operationId, cancellationToken);

	private async Task WatchAsync(
		CancellationToken cancellationToken)
	{
		await foreach (var list in client.WatchAsync(
			revision,
			cancellationToken))
		{
			Apply(list);
		}
	}

	public ValueTask DisposeAsync();
}
```

`FileOperationsModel` は Files のアプリケーションスコープです。WinUI、observable collection、ローカライズ文字列を持ちません。
各ウィンドウの `FileOperationsViewModel` が dispatcher 上で observable collection へ適応し、Status Center へ依存関係プロパティで trickle down します。

```csharp
public sealed partial class FileOperationsViewModel
	: ObservableObject,
		IDisposable
{
	public FileOperationsViewModel(
		FileOperationsModel model,
		IUiDispatcher dispatcher);

	public ReadOnlyObservableCollection<FileOperationViewModel>
		Items { get; }

	public void Dispose();
}
```

## コマンドからの開始

削除確認、完全削除、競合動作、新しい名前、昇格、資格情報は Files で確定してから送信します。

```csharp
public async ValueTask ExecuteAsync(
	CommandContext context,
	CancellationToken cancellationToken)
{
	var destination = await folderPicker.PickAsync(
		context.WindowId,
		cancellationToken);
	if (destination is null)
	{
		return;
	}

	var request = new FileOperationRequest(
		OperationId: Guid.NewGuid().ToString("N"),
		Kind: FileOperationKind.Copy,
		Items: context.Selection
			.Select(FileOperationReference.FromReference)
			.ToImmutableArray(),
		DestinationFolder:
			FileOperationReference.FromReference(destination),
		Name: null,
		CreatedItemKind: null,
		ConflictBehavior:
			StorageConflictBehavior.GenerateUniqueName,
		Permanently: false);

	await operations.SubmitAsync(
		request,
		cancellationToken);
}
```

開始後にコマンドハンドラーが `BrowseSession.Items` を変更してはいけません。
`IFolderChangeSource` と通常の参照セッション更新が表示を調整します。

## 最初の対応範囲

```text
Operation: copy, move, delete, create, rename
Source:    WindowsStorageSource
Queue:     Windows logical operation を 1 つずつ
Progress:  項目数。偽の byte percentage は出さない
History:   terminal snapshot を期限付きで保持
```

FTP はサーバーが保護された資格情報を自分で解決できるようになってから追加します。
アーカイブ変更、Quick Look、プレビュー、通常のファイルオープンはこのサーバーの責務ではありません。

## 実装順序

1. Core の操作値、WinRT struct、両側の mapper。
2. `.winmd` 生成と配列の ABI round-trip test。
3. `StorageRuntimeBuilder.AddWindowsOperations()`。
4. request reader、hash、validation tests。
5. `FileOperationSnapshots`、`FileOperation`、`FileOperationHost`。
6. versioned JSON journal、revision、lifetime。
7. `FileOperationServer` と explicit activation allowlist。
8. WinRT client、`FileOperationsModel`、Status Center。
9. copy 1 本を end-to-end で移行。
10. move/delete/create/rename、複数選択。
11. `AppInstanceMonitor`、server kill、古い直接実行経路を削除。

## 受け入れ条件

```text
1. Files が StartAsync を呼ぶ。
2. server が Queued を journal へ書く。
3. server-owned token で Windows 操作を開始する。
4. Files を強制終了する。
5. Files.Operations が操作を完了する。
6. 新しい Files が ListAsync で結果を取得する。
7. 表示中フォルダーは watcher から更新される。
```

```text
same OperationId + same hash      -> existing snapshot
same OperationId + different hash -> rejected
client disconnect                 -> operation continues
server restart + non-terminal     -> Unknown, never auto-resumed
partial failure                   -> per-item result remains
active operation                  -> idle exit is impossible
```
