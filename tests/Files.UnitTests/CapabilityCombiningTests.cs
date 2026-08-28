// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Text;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for item capability combining behavior.
/// </summary>
[TestClass]
public sealed class CapabilityCombiningTests
{
	/// <summary>
	/// Test case: thumbnail composition uses priority and stops after success.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ThumbnailCompositionUsesPriorityAndStopsAfterSuccess()
	{
		var context = CreateContext();
		var lower = new TestThumbnailSource(null);
		var higher = new TestThumbnailSource("higher");
		var combiner = new ThumbnailSourceCombiner();
		var source = combiner.Combine(context, [ new CapabilityOption<IThumbnailSource>(lower, 10, "lower", CapabilityLifetime.Shared), new CapabilityOption<IThumbnailSource>(higher, 20, "higher", CapabilityLifetime.Shared), ])!;

		var result = await source.GetThumbnailAsync(new ThumbnailRequest(64));
		Assert.IsNotNull(result);
		Assert.AreEqual("higher", Encoding.UTF8.GetString(result.Content.Span));
		Assert.AreEqual(1, higher.CallCount);
		Assert.AreEqual(0, lower.CallCount);
	}

	/// <summary>
	/// Test case: thumbnail composition falls back when higher priority returns null.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ThumbnailCompositionFallsBackWhenHigherPriorityReturnsNull()
	{
		var context = CreateContext();
		var first = new TestThumbnailSource(null);
		var second = new TestThumbnailSource("fallback");
		var source = new ThumbnailSourceCombiner().Combine(context, [ new CapabilityOption<IThumbnailSource>(first, 20, "first", CapabilityLifetime.Shared), new CapabilityOption<IThumbnailSource>(second, 10, "second", CapabilityLifetime.Shared), ])!;

		var result = await source.GetThumbnailAsync(new ThumbnailRequest(64));
		Assert.IsNotNull(result);
		Assert.AreEqual("fallback", Encoding.UTF8.GetString(result.Content.Span));
		Assert.AreEqual(1, first.CallCount);
		Assert.AreEqual(1, second.CallCount);
	}

	/// <summary>
	/// Test case: preview composition routes by priority.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PreviewCompositionRoutesByPriority()
	{
		var context = CreateContext();
		var first = new TestPreviewSource(null);
		var second = new TestPreviewSource("preview");
		var source = new PreviewSourceCombiner().Combine(context, [ new CapabilityOption<IPreviewSource>(first, 5, "first", CapabilityLifetime.Shared), new CapabilityOption<IPreviewSource>(second, 1, "second", CapabilityLifetime.Shared), ])!;

		await using var result = await source.GetPreviewAsync(new PreviewRequest());
		Assert.IsNotNull(result);
		var streamResult = result as StreamPreviewResult;
		Assert.IsNotNull(streamResult);
		Assert.AreEqual("preview", await ReadTextAsync(streamResult!.Content));
	}

	/// <summary>
	/// Test case: preview composition stops fallback after blocked result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PreviewCompositionStopsFallbackAfterBlockedResult()
	{
		var context = CreateContext();
		var blocked = new BlockedPreviewSource();
		var fallback = new TestPreviewSource("fallback");
		var source = new PreviewSourceCombiner().Combine(context, [ new CapabilityOption<IPreviewSource>(blocked, 20, "blocked", CapabilityLifetime.Shared), new CapabilityOption<IPreviewSource>(fallback, 10, "fallback", CapabilityLifetime.Shared), ])!;

		await using var result = await source.GetPreviewAsync(new PreviewRequest());

		Assert.IsInstanceOfType<BlockedPreviewResult>(result);
		Assert.AreEqual(0, fallback.CallCount);
	}

	/// <summary>
	/// Test case: property composition merges sources with higher priority winning.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task PropertyCompositionMergesSourcesWithHigherPriorityWinning()
	{
		var context = CreateContext();
		var low = new TestPropertySource(new Dictionary<string, object?> { ["name"] = "low", ["lowOnly"] = true, });
		var high = new TestPropertySource(new Dictionary<string, object?> { ["name"] = "high", ["Name"] = "case-sensitive", });
		var source = new PropertySourceCombiner().Combine(context, [ new CapabilityOption<IPropertySource>(low, 10, "low", CapabilityLifetime.Shared), new CapabilityOption<IPropertySource>(high, 20, "high", CapabilityLifetime.Shared), ])!;

		var values = await source.GetPropertiesAsync(new PropertyRequest(["name", "Name", "lowOnly"]));
		Assert.AreEqual("high", values["name"]);
		Assert.AreEqual("case-sensitive", values["Name"]);
		Assert.AreEqual(true, values["lowOnly"]);
}

	private static ItemContext CreateContext()
	{
		var source = new TestStorageSource();
		var coreModel = new TestStorable("item", "Item");

		return new ItemContext(source, coreModel, new Files.Core.Storage.StorableReference(source.SourceId, coreModel.Id));
	}

	private static async Task<string> ReadTextAsync(Stream stream)
	{
		using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

		return await reader.ReadToEndAsync();
	}

	private sealed class TestThumbnailSource : IThumbnailSource
	{
		private readonly string? value;

		public int CallCount { get; private set; }

		public TestThumbnailSource(string? value) => this.value = value;

		public ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
		{
			CallCount++;

			return ValueTask.FromResult<ThumbnailResult?>(value is null ? null : new ThumbnailResult(Encoding.UTF8.GetBytes(value), "text/plain", false));
		}
	}

		private sealed class TestPreviewSource : IPreviewSource
	{
		private readonly string? value;

		public int CallCount { get; private set; }

		public TestPreviewSource(string? value) => this.value = value;

		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
		{
			CallCount++;

			return ValueTask.FromResult<PreviewResult?>(value is null ? null : new StreamPreviewResult(new MemoryStream(Encoding.UTF8.GetBytes(value), writable: false), "text/plain"));
		}
	}

	private sealed class BlockedPreviewSource : IPreviewSource
	{
		public ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<PreviewResult?>(new BlockedPreviewResult(PreviewBlockReason.RequiresHydration));
	}

	private sealed class TestPropertySource : IPropertySource
	{
		private readonly IReadOnlyDictionary<string, object?> values;

		public TestPropertySource(IReadOnlyDictionary<string, object?> values) => this.values = values;

		public ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(values);
	}
}
