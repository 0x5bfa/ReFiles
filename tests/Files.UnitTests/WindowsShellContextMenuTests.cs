// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Text;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Protects context-menu composition, command identity, and popup-session lifetime.</summary>
[TestClass]
public sealed class WindowsShellContextMenuTests
{
	/// <summary>Cloud commands and their provider remain together without absorbing ordinary extensions.</summary>
	[TestMethod]
	public void CloudSectionDoesNotAbsorbOrdinaryExtensionGroups()
	{
		var properties = WindowsShellContextMenuEntry.CreateApplicationCommand("properties", "Properties", true);
		var pin = new WindowsShellContextMenuEntry("MakeAvailableOffline", "Always keep on this device", true);
		var free = new WindowsShellContextMenuEntry("MakeAvailableOnline", "Free up space", true);
		var nanaZip = new WindowsShellContextMenuEntry("zip", "NanaZip", true);
		var providerCommand = new WindowsShellContextMenuEntry("share", "Share", true);
		var more = WindowsShellContextMenuEntry.CreateApplicationCommand("more", "Show more options", true);
		WindowsShellContextMenuGroup[] groups = [new("aaa-nanazip", "NanaZip", false, [nanaZip]), new("zzz-provider", "OneDrive", true, [providerCommand], new("provider.png"))];
		var entries = WindowsShellContextMenuBuilder.Build([properties], [pin, free], groups, more);
		CollectionAssert.AreEqual(new[] { "Properties", "", pin.Title, free.Title, "OneDrive", "", "NanaZip", "", more.Title }, entries.Select(static entry => entry.Title).ToArray());
		Assert.AreSame(providerCommand, entries[4].Children.Single());
		Assert.AreEqual("provider.png", entries[4].Icon!.Path);
		Assert.AreSame(nanaZip, entries[6]);
	}

	/// <summary>Extension display names control parent ordering independently of package identities and child labels.</summary>
	[TestMethod]
	public void OrdinaryGroupsUseDisplayNamesAndPreserveChildOrder()
	{
		var first = new WindowsShellContextMenuEntry("first", "Z first", true);
		var second = new WindowsShellContextMenuEntry("second", "A second", true);
		var flat = new WindowsShellContextMenuEntry("flat", "Flat command", true);
		WindowsShellContextMenuGroup[] groups = [new("alpha-package", "Zulu group", false, [first, second]), new("Zulu-package", "Alpha extension", false, [flat])];
		var entries = WindowsShellContextMenuBuilder.Build([], [], groups, null);
		Assert.AreSame(flat, entries[0]);
		CollectionAssert.AreEqual(new[] { first, second }, entries[1].Children.ToArray());
	}

	/// <summary>Empty contributions do not leave leading, repeated, or trailing separators.</summary>
	[TestMethod]
	public void SeparatorsAreNormalizedAcrossEmptySections()
	{
		var separator = WindowsShellContextMenuEntry.CreateSeparator();
		var command = WindowsShellContextMenuEntry.CreateApplicationCommand("app", "Application", false);
		var entries = WindowsShellContextMenuBuilder.Build([separator, command, separator, separator], [], [new("empty", "Empty", false, [])], null);
		CollectionAssert.AreEqual(new[] { command }, entries.ToArray());
		Assert.IsFalse(entries[0].IsEnabled);
	}

	/// <summary>Shell mnemonics, escaped ampersands, and accelerator descriptions remain distinct.</summary>
	[TestMethod]
	[DataRow("Open w&ith Code", "Open with Code", "i", null)]
	[DataRow("名前(&N)\tAlt+N", "名前", "N", "Alt+N")]
	[DataRow("Research && Development", "Research & Development", null, null)]
	public void ShellLabelsPreserveMnemonicMeaning(string input, string title, string? key, string? shortcut)
	{
		var label = WindowsShellContextMenuEntry.ParseShellLabel(input);
		Assert.AreEqual(title, label.Title);
		Assert.AreEqual(key, label.AccessKey);
		Assert.AreEqual(shortcut, label.Shortcut);
		Assert.AreEqual(input, WindowsShellContextMenuEntry.CreateApplicationCommand("app", input, true).Title);
	}

	/// <summary>Canonical verbs are recovered from providers that ignore GCS_VERBW and return ANSI bytes.</summary>
	[TestMethod]
	public void CanonicalVerbSupportsAnsiReturnedForUnicodeRequest()
	{
		Span<char> buffer = stackalloc char[128];
		buffer.Clear();
		Encoding.ASCII.GetBytes("MakeAvailableOffline", MemoryMarshal.AsBytes(buffer));
		Assert.AreEqual("MakeAvailableOffline", WindowsShellContextMenuCommandHelper.DecodeCanonicalVerb(buffer));
		Assert.AreEqual("MakeAvailableOnline", WindowsShellContextMenuCommandHelper.DecodeCanonicalVerb("MakeAvailableOnline\0"));
	}

	/// <summary>Cloud pin icons use Explorer's installed SVG assets for every supported theme.</summary>
	[TestMethod]
	[TestCategory("WindowsIntegration")]
	public void CloudPinIconsUseInstalledThemeAssets()
	{
		var light = WindowsShellContextMenuAssets.Read("MakeAvailableOffline", WindowsShellContextMenuIconTheme.Light);
		if (light.IsEmpty)
		{
			Assert.Inconclusive("The FileExplorerExtensions asset package is not registered on this Windows installation.");
		}

		foreach (var theme in Enum.GetValues<WindowsShellContextMenuIconTheme>())
		{
			foreach (var verb in new[] { "MakeAvailableOffline", "MakeAvailableOnline" })
			{
				var icon = WindowsShellContextMenuAssets.Read(verb, theme);
				Assert.IsTrue(Encoding.UTF8.GetString(icon.Span).Contains("<svg", StringComparison.Ordinal), $"{verb}: {theme}");
			}
		}

		var dark = WindowsShellContextMenuAssets.Read("MakeAvailableOffline", WindowsShellContextMenuIconTheme.Dark);
		Assert.IsFalse(light.Span.SequenceEqual(dark.Span));
		Assert.IsTrue(WindowsShellContextMenuAssets.Read("UnregisteredVerb", WindowsShellContextMenuIconTheme.Light).IsEmpty);
	}

	/// <summary>A popup session accepts only its own native commands and safely finishes queued icon work during teardown.</summary>
	/// <returns>The asynchronous verification.</returns>
	[TestMethod]
	[TestCategory("WindowsIntegration")]
	public async Task SessionRejectsForeignCommandsAndClosesAfterQueuedWork()
	{
		var path = Path.GetTempFileName();
		try
		{
			await using var source = new WindowsStorageSource();
			var address = new StorageAddress(WindowsStorageSource.FileAddressScheme, path);
			var item = await source.ResolveAsync(address);
			var application = WindowsShellContextMenuEntry.CreateApplicationCommand("app", "Application", true);
			await using var session = await WindowsShellContextMenuSession.CreateAsync(source, [new(source.SourceId, item.Id, address)], applicationEntries: [application]);
			Assert.IsNotNull(session);
			Assert.AreSame(application, session.Entries[0]);
			Assert.IsFalse(await session.InvokeAsync(new("foreign", "Foreign command", true)));
			var icons = session.GetIconAsync(application, 16);
			await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());
			Assert.IsTrue((await icons).IsEmpty);
			Assert.Throws<ObjectDisposedException>(() => session.GetIconAsync(application, 16));
		}
		finally
		{
			File.Delete(path);
		}
	}
}
