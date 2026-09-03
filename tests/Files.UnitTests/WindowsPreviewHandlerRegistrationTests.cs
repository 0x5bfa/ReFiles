// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Security;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Microsoft.Win32;
using OwlCore.Storage;
using Windows.Win32.Foundation;

namespace Files.UnitTests;

/// <summary>Contains tests for Windows preview handler association and registration validation.</summary>
[TestClass]
public sealed class WindowsPreviewHandlerRegistrationTests
{
	/// <summary>Test case: a resolver returns an associated handler present in the registration allowlist.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ResolverReturnsRegisteredAssociatedHandler()
	{
		var handlerClsid = Guid.NewGuid();
		var association = new FakeAssociation(handlerClsid.ToString("B"));
		var allowlist = new FakeRegistrationAllowlist(handlerClsid);
		var resolver = new WindowsPreviewHandlerResolver(association, allowlist);

		var result = await resolver.ResolveAsync(CreateContext("document.pdf"));

		Assert.AreEqual(handlerClsid, result);
		Assert.AreEqual(1, association.CallCount);
		Assert.AreEqual(1, allowlist.CallCount);
	}

	/// <summary>Test case: a resolver rejects and caches an associated handler missing from the registration allowlist.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ResolverRejectsUnregisteredAssociatedHandler()
	{
		var handlerClsid = Guid.NewGuid();
		var association = new FakeAssociation(handlerClsid.ToString("B"));
		var allowlist = new FakeRegistrationAllowlist(null);
		var resolver = new WindowsPreviewHandlerResolver(association, allowlist);
		var context = CreateContext("document.pdf");

		Assert.IsNull(await resolver.ResolveAsync(context));
		Assert.IsNull(await resolver.ResolveAsync(context));
		Assert.AreEqual(1, association.CallCount);
		Assert.AreEqual(1, allowlist.CallCount);
	}

	/// <summary>Test case: malformed association data is rejected without consulting the registration allowlist.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ResolverDoesNotCheckRegistrationForMalformedAssociation()
	{
		var association = new FakeAssociation("not-a-guid");
		var allowlist = new FakeRegistrationAllowlist(Guid.NewGuid());
		var resolver = new WindowsPreviewHandlerResolver(association, allowlist);

		var result = await resolver.ResolveAsync(CreateContext("document.pdf"));

		Assert.IsNull(result);
		Assert.AreEqual(0, allowlist.CallCount);
	}

	/// <summary>Test case: an oversized association response is rejected before allocating its reported buffer.</summary>
	[TestMethod]
	public void AssociationRejectsOversizedReportedLength()
	{
		var callCount = 0;
		var association = new WindowsShellPreviewHandlerAssociation(QueryAssociation);

		var result = association.QueryPreviewHandler(".PDF");

		Assert.IsNull(result);
		Assert.AreEqual(1, callCount);

		HRESULT QueryAssociation(string normalizedExtension, Span<char> buffer, ref uint characterCount)
		{
			callCount++;
			characterCount = WindowsShellPreviewHandlerAssociation.MaximumAssociationCharacterCount + 1;

			return HRESULT.S_FALSE;
		}
	}

	/// <summary>Test case: an association response that grows between queries is rejected.</summary>
	[TestMethod]
	public void AssociationRejectsLengthGrowthBetweenQueries()
	{
		var callCount = 0;
		var association = new WindowsShellPreviewHandlerAssociation(QueryAssociation);

		var result = association.QueryPreviewHandler(".PDF");

		Assert.IsNull(result);
		Assert.AreEqual(2, callCount);

		HRESULT QueryAssociation(string normalizedExtension, Span<char> buffer, ref uint characterCount)
		{
			callCount++;
			if (buffer.IsEmpty)
			{
				characterCount = 39;

				return HRESULT.S_FALSE;
			}

			characterCount = 40;

			return HRESULT.S_OK;
		}
	}

	/// <summary>Test case: a bounded association response is returned unchanged.</summary>
	[TestMethod]
	public void AssociationReturnsBoundedHandlerClsid()
	{
		var handlerClsid = Guid.NewGuid();
		var associationValue = $"{handlerClsid:B}\0";
		var association = new WindowsShellPreviewHandlerAssociation(QueryAssociation);

		var result = association.QueryPreviewHandler(".PDF");

		Assert.AreEqual(handlerClsid.ToString("B"), result);

		HRESULT QueryAssociation(string normalizedExtension, Span<char> buffer, ref uint characterCount)
		{
			characterCount = (uint)associationValue.Length;
			if (buffer.IsEmpty)
			{
				return HRESULT.S_FALSE;
			}

			associationValue.AsSpan().CopyTo(buffer);

			return HRESULT.S_OK;
		}
	}

	/// <summary>Test case: the registry allowlist checks the current-user registration before the machine-wide registration.</summary>
	[TestMethod]
	public void RegistryAllowlistChecksCurrentUserThenLocalMachine()
	{
		var handlerClsid = Guid.NewGuid();
		var requests = new List<(RegistryHive Hive, string ValueName)>();
		var allowlist = new WindowsPreviewHandlerRegistrationAllowlist(IsRegistered);

		bool IsRegistered(RegistryHive hive, string valueName)
		{
			requests.Add((hive, valueName));

			return hive is RegistryHive.LocalMachine;
		}

		var result = allowlist.IsRegistered(handlerClsid);

		Assert.IsTrue(result);
		CollectionAssert.AreEqual(new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }, requests.Select(static request => request.Hive).ToArray());
		Assert.IsTrue(requests.All(request => string.Equals(request.ValueName, handlerClsid.ToString("B"), StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>Test case: an inaccessible registration hive fails closed without hiding a registration in the other hive.</summary>
	[TestMethod]
	public void RegistryAllowlistContinuesAfterInaccessibleHive()
	{
		var handlerClsid = Guid.NewGuid();
		var allowlist = new WindowsPreviewHandlerRegistrationAllowlist((hive, valueName) =>
		{
			if (hive is RegistryHive.CurrentUser)
			{
				throw new SecurityException("Registry access denied.");
			}

			return true;
		});

		Assert.IsTrue(allowlist.IsRegistered(handlerClsid));
	}

	/// <summary>Test case: the registry allowlist accepts only REG_SZ registrations.</summary>
	[TestMethod]
	public void RegistryAllowlistRequiresStringValueKind()
	{
		Assert.IsTrue(WindowsPreviewHandlerRegistrationAllowlist.IsRegistrationValueKind(RegistryValueKind.String));
		Assert.IsFalse(WindowsPreviewHandlerRegistrationAllowlist.IsRegistrationValueKind(RegistryValueKind.ExpandString));
		Assert.IsFalse(WindowsPreviewHandlerRegistrationAllowlist.IsRegistrationValueKind(RegistryValueKind.Binary));
	}

	private static ItemContext CreateContext(string name)
	{
		var source = new TestStorageSource();
		var file = new TestWindowsFile("item", name);

		return new ItemContext(source, file, new StorableReference(source.SourceId, file.Id));
	}

	private sealed class FakeAssociation : IWindowsPreviewHandlerAssociation
	{
		public string? Value { get; }

		public int CallCount { get; private set; }

		public FakeAssociation(string? value)
		{
			Value = value;
		}

		public string? QueryPreviewHandler(string normalizedExtension)
		{
			CallCount++;

			return Value;
		}
	}

	private sealed class FakeRegistrationAllowlist : IWindowsPreviewHandlerRegistrationAllowlist
	{
		private readonly Guid? _registeredClsid;

		public int CallCount { get; private set; }

		public FakeRegistrationAllowlist(Guid? registeredClsid)
		{
			_registeredClsid = registeredClsid;
		}

		public bool IsRegistered(Guid handlerClsid)
		{
			CallCount++;

			return handlerClsid == _registeredClsid;
		}
	}

	private sealed class TestWindowsFile : TestStorable, IWindowsStorable, IFile
	{
		public StorageAddress Address { get; }

		public string ParsingName => Address.Value;

		public string? FileSystemPath => Address.Value;

		public bool IsFileSystem => true;

		public bool IsStream => true;

		public TestWindowsFile(string id, string name)
			: base(id, name)
		{
			Address = new StorageAddress("file", name);
		}

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default) => Task.FromResult<IFolder?>(null);

		public Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());
	}
}
