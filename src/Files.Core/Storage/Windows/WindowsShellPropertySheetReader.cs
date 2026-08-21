// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authorization;
using Windows.Win32.Security.Cryptography;
using Windows.Win32.Security.Cryptography.Catalog;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsShellPropertySheetReader
{
	private const byte AccessAllowedAceType = 0;
	private const byte AccessDeniedAceType = 1;
	private const uint ErrorMoreData = 234;
	private const uint MaximumPreferredLength = uint.MaxValue;
	private const int ShellStringCapacity = 32_768;
	private const uint ShellLinkRawPath = 4;
	private const uint CryptographicMessageSignerCount = 5;
	private const uint CryptographicMessageSignerInfo = 6;
	private const uint CertificateNameSimpleDisplayType = 4;
	private const uint SnapshotControlCode = 0x00144064;
	private const int SnapshotHeaderSize = 12;
	private const int SnapshotNameSize = 50;
	private const string SnapshotNameFormat = "'@GMT-'yyyy.MM.dd-HH.mm.ss";

	internal static WindowsShellPropertySheetData CreateEmpty(IReadOnlyList<WindowsShellPropertyPage> pages)
	{
		return new(pages, null, null, null, [], null, [], [], []);
	}

	internal static WindowsShellPropertySheetData Read(IShellItem primaryItem, WindowsShellResolvedSelection selection, IReadOnlyList<WindowsShellPropertyPage> pages, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(primaryItem);
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(pages);

		cancellationToken.ThrowIfCancellationRequested();
		var primaryPath = selection.FileSystemPaths.Count is 1 ? selection.FileSystemPaths[0] : null;
		var shortcut = primaryPath is null ? null : TryReadShortcut(primaryPath);
		cancellationToken.ThrowIfCancellationRequested();
		var sharing = selection.IsSingleFolder && primaryPath is not null ? ReadSharing(primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsSecurity = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Security);
		var security = readsSecurity && primaryPath is not null ? TryReadSecurity(primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsPreviousVersions = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.PreviousVersions);
		var previousVersions = readsPreviousVersions && primaryPath is not null ? ReadPreviousVersions(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var customization = selection.IsSingleFolder && primaryPath is not null ? TryReadFolderCustomization(primaryItem, primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsSignatures = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.DigitalSignatures);
		var embeddedSignatures = readsSignatures && primaryPath is not null && File.Exists(primaryPath) ? ReadEmbeddedSignatures(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var catalogSignatures = readsSignatures && primaryPath is not null && File.Exists(primaryPath) ? ReadCatalogSignatures(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var readsDetails = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Details);
		var details = readsDetails ? ReadDetails(primaryItem) : [];

		return new(pages, shortcut, sharing, security, previousVersions, customization, embeddedSignatures, catalogSignatures, details);
	}

	private static WindowsShellShortcutProperties? TryReadShortcut(string path)
	{
		if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		try
		{
			var link = ShellLink.CreateInstance<IShellLinkW>();
			if (link is not IPersistFile persistedLink || persistedLink.Load(path, STGM.STGM_READ).Failed)
			{
				return null;
			}

			Span<char> targetBuffer = stackalloc char[ShellStringCapacity];
			Span<char> argumentsBuffer = stackalloc char[ShellStringCapacity];
			Span<char> workingDirectoryBuffer = stackalloc char[ShellStringCapacity];
			Span<char> commentBuffer = stackalloc char[ShellStringCapacity];
			Span<char> iconBuffer = stackalloc char[ShellStringCapacity];
			var findData = new WIN32_FIND_DATAW();
			link.GetPath(targetBuffer, ref findData, ShellLinkRawPath);
			link.GetArguments(argumentsBuffer);
			link.GetWorkingDirectory(workingDirectoryBuffer);
			link.GetDescription(commentBuffer);
			link.GetHotkey(out var hotkey);
			link.GetShowCmd(out var showCommand);
			link.GetIconLocation(iconBuffer, out var iconIndex);
			var targetPath = ReadNullTerminated(targetBuffer);
			var targetType = ReadShellType(targetPath);
			var targetLocation = string.IsNullOrEmpty(targetPath) ? string.Empty : new FileInfo(targetPath).Directory?.Name ?? string.Empty;

			return new(
				targetPath,
				targetType,
				targetLocation,
				ReadNullTerminated(argumentsBuffer),
				ReadNullTerminated(workingDirectoryBuffer),
				hotkey,
				(int)showCommand,
				ReadNullTerminated(commentBuffer),
				ReadNullTerminated(iconBuffer),
				iconIndex);
		}
		catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static string ReadShellType(string path)
	{
		if (string.IsNullOrEmpty(path) || PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem shellItem).Failed)
		{
			return string.Empty;
		}

		return ReadShellString(shellItem, "System.ItemTypeText") ?? string.Empty;
	}

	private static WindowsShellSharingProperties ReadSharing(string path)
	{
		var normalizedPath = NormalizePath(path);
		var bestShareName = string.Empty;
		var bestSharePath = string.Empty;
		uint resumeHandle = 0;
		do
		{
			byte* buffer = null;
			var result = PInvoke.NetShareEnum(default, 2, out buffer, MaximumPreferredLength, out var entriesRead, out _, ref resumeHandle);
			try
			{
				if (buffer is null || result is not 0 and not ErrorMoreData)
				{
					break;
				}

				var shares = (SHARE_INFO_2*)buffer;
				for (var index = 0u; index < entriesRead; index++)
				{
					var sharePath = NormalizePath(shares[index].shi2_path.ToString());
					if (sharePath.Length > bestSharePath.Length && IsPathWithin(normalizedPath, sharePath))
					{
						bestSharePath = sharePath;
						bestShareName = shares[index].shi2_netname.ToString();
					}
				}
			}
			finally
			{
				if (buffer is not null)
				{
					PInvoke.NetApiBufferFree(buffer);
				}
			}

			if (result is not ErrorMoreData)
			{
				break;
			}
		}
		while (true);

		if (string.IsNullOrEmpty(bestSharePath))
		{
			return new(false, string.Empty, string.Empty);
		}

		var relativePath = normalizedPath[bestSharePath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var networkPath = $"\\\\{Environment.MachineName}\\{bestShareName}";
		if (!string.IsNullOrEmpty(relativePath))
		{
			networkPath = Path.Combine(networkPath, relativePath);
		}

		return new(true, bestShareName, networkPath);
	}

	private static WindowsShellSecurityProperties? TryReadSecurity(string path)
	{
		ACL* discretionaryAcl = null;
		PSECURITY_DESCRIPTOR securityDescriptor = default;
		fixed (char* pathPointer = path)
		{
			var result = PInvoke.GetNamedSecurityInfo(
				new PCWSTR(pathPointer),
				SE_OBJECT_TYPE.SE_FILE_OBJECT,
				OBJECT_SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
				null,
				null,
				&discretionaryAcl,
				null,
				&securityDescriptor);
			if (result != WIN32_ERROR.ERROR_SUCCESS || securityDescriptor.IsNull)
			{
				return null;
			}
		}

		try
		{
			if (discretionaryAcl is null || !PInvoke.IsValidAcl(*discretionaryAcl))
			{
				return new(path, []);
			}

			var aclInformation = new ACL_SIZE_INFORMATION();
			if (!PInvoke.GetAclInformation(discretionaryAcl, &aclInformation, checked((uint)sizeof(ACL_SIZE_INFORMATION)), ACL_INFORMATION_CLASS.AclSizeInformation))
			{
				return null;
			}

			var principals = new Dictionary<string, SecurityPrincipalBuilder>(StringComparer.OrdinalIgnoreCase);
			for (var index = 0u; index < aclInformation.AceCount; index++)
			{
				if (!PInvoke.GetAce(*discretionaryAcl, index, out var acePointer) || acePointer is null)
				{
					continue;
				}

				var ace = (ACCESS_ALLOWED_ACE*)acePointer;
				if (ace->Header.AceType is not AccessAllowedAceType and not AccessDeniedAceType)
				{
					continue;
				}

				var sid = new PSID(&ace->SidStart);
				var sidText = ReadSid(sid);
				if (string.IsNullOrEmpty(sidText))
				{
					continue;
				}

				if (!principals.TryGetValue(sidText, out var principal))
				{
					principal = new(ReadAccountName(sid, sidText), sidText);
					principals.Add(sidText, principal);
				}

				if (ace->Header.AceType is AccessAllowedAceType)
				{
					principal.AllowedAccessMask |= ace->Mask;
				}
				else
				{
					principal.DeniedAccessMask |= ace->Mask;
				}
			}

			return new(path, principals.Values.Select(static principal => principal.Create()).ToArray());
		}
		finally
		{
			PInvoke.LocalFree(new HLOCAL((nint)securityDescriptor.Value));
		}
	}

	private static string ReadSid(PSID sid)
	{
		if (!PInvoke.ConvertSidToStringSid(sid, out var value) || value.Value is null)
		{
			return string.Empty;
		}

		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.LocalFree(new HLOCAL((nint)value.Value));
		}
	}

	private static string ReadAccountName(PSID sid, string fallback)
	{
		uint nameLength = 0;
		uint domainLength = 0;
		PInvoke.LookupAccountSid(null!, sid, [], ref nameLength, [], ref domainLength, out _);
		if (nameLength is 0)
		{
			return fallback;
		}

		var name = new char[nameLength];
		var domain = new char[domainLength];
		if (!PInvoke.LookupAccountSid(null!, sid, name, ref nameLength, domain, ref domainLength, out _))
		{
			return fallback;
		}

		var accountName = ReadNullTerminated(name);
		var domainName = ReadNullTerminated(domain);

		return string.IsNullOrEmpty(domainName) ? accountName : $"{domainName}\\{accountName}";
	}

	private static WindowsShellFolderCustomizationProperties? TryReadFolderCustomization(IShellItem shellItem, string path)
	{
		Span<char> iconBuffer = stackalloc char[ShellStringCapacity];
		Span<char> pictureBuffer = stackalloc char[ShellStringCapacity];
		fixed (char* iconPointer = iconBuffer)
		fixed (char* picturePointer = pictureBuffer)
		{
			var settings = new SHFOLDERCUSTOMSETTINGS
			{
				dwSize = checked((uint)sizeof(SHFOLDERCUSTOMSETTINGS)),
				dwMask = PInvoke.FCSM_ICONFILE | PInvoke.FCSM_LOGO,
				pszIconFile = new PWSTR(iconPointer),
				cchIconFile = checked((uint)iconBuffer.Length),
				pszLogo = new PWSTR(picturePointer),
				cchLogo = checked((uint)pictureBuffer.Length),
			};
			if (PInvoke.SHGetSetFolderCustomSettings(ref settings, path, PInvoke.FCS_READ).Failed)
			{
				return null;
			}

			var folderKind = ReadShellString(shellItem, "System.FolderKind") ?? string.Empty;

			return new(folderKind, ReadNullTerminated(pictureBuffer), ReadNullTerminated(iconBuffer), settings.iIconIndex);
		}
	}

	private static IReadOnlyList<WindowsShellPreviousVersion> ReadPreviousVersions(string path)
	{
		using var file = PInvoke.CreateFile(
			path,
			(uint)FILE_ACCESS_RIGHTS.FILE_LIST_DIRECTORY,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
			null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
			null);
		if (file.IsInvalid)
		{
			return [];
		}

		Span<byte> header = stackalloc byte[16];
		var initialResult = PInvoke.DeviceIoControl(file, SnapshotControlCode, [], header, out _, null);
		var initialError = initialResult ? WIN32_ERROR.ERROR_SUCCESS : (WIN32_ERROR)Marshal.GetLastPInvokeError();
		if (!initialResult && initialError is not WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER and not WIN32_ERROR.ERROR_MORE_DATA)
		{
			return [];
		}

		var count = BinaryPrimitives.ReadUInt32LittleEndian(header);
		var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
		if (count is 0 || payloadSize < (ulong)count * SnapshotNameSize || payloadSize > int.MaxValue - SnapshotHeaderSize)
		{
			return [];
		}

		var output = new byte[checked((int)payloadSize + SnapshotHeaderSize)];
		if (!PInvoke.DeviceIoControl(file, SnapshotControlCode, [], output, out _, null))
		{
			return [];
		}

		count = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output), count);
		var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var versions = new List<WindowsShellPreviousVersion>(checked((int)count));
		var names = output.AsSpan(SnapshotHeaderSize);
		for (var index = 0u; index < count; index++)
		{
			var offset = checked((int)index * SnapshotNameSize);
			if (offset + SnapshotNameSize > names.Length)
			{
				break;
			}

			var snapshotName = ReadNullTerminated(MemoryMarshal.Cast<byte, char>(names.Slice(offset, SnapshotNameSize)));
			if (DateTimeOffset.TryParseExact(snapshotName, SnapshotNameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
			{
				versions.Add(new(name, snapshotName, timestamp.ToLocalTime()));
			}
		}

		return versions.OrderByDescending(static version => version.DateModified).ToArray();
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadEmbeddedSignatures(string path)
	{
		return ReadCryptographicSignatures(path, CERT_QUERY_CONTENT_TYPE_FLAGS.CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED, string.Empty);
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadCryptographicSignatures(string path, CERT_QUERY_CONTENT_TYPE_FLAGS contentType, string catalogPath)
	{
		HCERTSTORE certificateStore = default;
		CERT_QUERY_ENCODING_TYPE encoding;
		void* message = null;
		fixed (char* pathPointer = path)
		{
			if (!PInvoke.CryptQueryObject(
				CERT_QUERY_OBJECT_TYPE.CERT_QUERY_OBJECT_FILE,
				pathPointer,
				contentType,
				CERT_QUERY_FORMAT_TYPE_FLAGS.CERT_QUERY_FORMAT_FLAG_BINARY,
				0,
				out encoding,
				out _,
				out _,
				out certificateStore,
				out message,
				out _))
			{
				return [];
			}
		}

		try
		{
			uint signerCount = 0;
			var countSize = checked((uint)sizeof(uint));
			if (message is null || !PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerCount, 0, new Span<byte>(&signerCount, sizeof(uint)), ref countSize))
			{
				return [];
			}

			var signatures = new List<WindowsShellDigitalSignature>(checked((int)signerCount));
			for (var index = 0u; index < signerCount; index++)
			{
				var signer = ReadSignerName(certificateStore, encoding, message, index);
				var algorithm = ReadSignerDigestAlgorithm(message, index);
				signatures.Add(new(signer, algorithm, string.Empty, catalogPath));
			}

			return signatures;
		}
		finally
		{
			if (message is not null)
			{
				PInvoke.CryptMsgClose(message);
			}

			if (!certificateStore.IsNull)
			{
				PInvoke.CertCloseStore(certificateStore, 0);
			}
		}
	}

	private static string ReadSignerName(HCERTSTORE certificateStore, CERT_QUERY_ENCODING_TYPE encoding, void* message, uint index)
	{
		uint size = 0;
		PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, [], ref size);
		if (size is 0)
		{
			return string.Empty;
		}

		var buffer = NativeMemory.Alloc(size);
		try
		{
			if (!PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, new Span<byte>(buffer, checked((int)size)), ref size))
			{
				return string.Empty;
			}

			var signerInfo = (CMSG_SIGNER_INFO*)buffer;
			var certificateInfo = new CERT_INFO { Issuer = signerInfo->Issuer, SerialNumber = signerInfo->SerialNumber };
			var certificate = PInvoke.CertFindCertificateInStore(certificateStore, encoding, 0, CERT_FIND_FLAGS.CERT_FIND_SUBJECT_CERT, &certificateInfo, (CERT_CONTEXT*)null);
			if (certificate is null)
			{
				return string.Empty;
			}

			try
			{
				var nameLength = PInvoke.CertGetNameString(in *certificate, CertificateNameSimpleDisplayType, 0, null, []);
				if (nameLength <= 1)
				{
					return string.Empty;
				}

				var name = new char[nameLength];
				PInvoke.CertGetNameString(in *certificate, CertificateNameSimpleDisplayType, 0, null, name);

				return ReadNullTerminated(name);
			}
			finally
			{
				PInvoke.CertFreeCertificateContext(certificate);
			}
		}
		finally
		{
			NativeMemory.Free(buffer);
		}
	}

	private static string ReadSignerDigestAlgorithm(void* message, uint index)
	{
		uint size = 0;
		PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, [], ref size);
		if (size is 0)
		{
			return string.Empty;
		}

		var buffer = NativeMemory.Alloc(size);
		try
		{
			if (!PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, new Span<byte>(buffer, checked((int)size)), ref size))
			{
				return string.Empty;
			}

			var oidValue = ((CMSG_SIGNER_INFO*)buffer)->HashAlgorithm.pszObjId.ToString();
			if (string.IsNullOrEmpty(oidValue))
			{
				return string.Empty;
			}

			return Oid.FromOidValue(oidValue, OidGroup.HashAlgorithm).FriendlyName ?? oidValue;
		}
		catch (CryptographicException)
		{
			return string.Empty;
		}
		finally
		{
			NativeMemory.Free(buffer);
		}
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadCatalogSignatures(string path)
	{
		var catalogPath = FindCatalogPath(path, "SHA256") ?? FindCatalogPath(path, "SHA1");
		if (string.IsNullOrEmpty(catalogPath))
		{
			return [];
		}

		return ReadCryptographicSignatures(catalogPath, CERT_QUERY_CONTENT_TYPE_FLAGS.CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED, catalogPath);
	}

	private static string? FindCatalogPath(string path, string hashAlgorithm)
	{
		if (!PInvoke.CryptCATAdminAcquireContext2(out var catalogAdmin, null, hashAlgorithm, null))
		{
			return null;
		}

		try
		{
			using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
			uint hashSize = 0;
			if (!PInvoke.CryptCATAdminCalcHashFromFileHandle2(catalogAdmin, file, ref hashSize, []) || hashSize is 0)
			{
				return null;
			}

			var hash = new byte[hashSize];
			if (!PInvoke.CryptCATAdminCalcHashFromFileHandle2(catalogAdmin, file, ref hashSize, hash))
			{
				return null;
			}

			var catalogContext = PInvoke.CryptCATAdminEnumCatalogFromHash(catalogAdmin, hash);
			if (catalogContext is 0)
			{
				return null;
			}

			try
			{
				var catalogInformation = new CATALOG_INFO { cbStruct = checked((uint)sizeof(CATALOG_INFO)) };
				if (!PInvoke.CryptCATCatalogInfoFromContext(catalogContext, ref catalogInformation, 0))
				{
					return null;
				}

				return ReadNullTerminated(catalogInformation.wszCatalogFile.AsSpan());
			}
			finally
			{
				PInvoke.CryptCATAdminReleaseCatalogContext(catalogAdmin, catalogContext, 0);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
		finally
		{
			PInvoke.CryptCATAdminReleaseContext(catalogAdmin, 0);
		}
	}

	private static IReadOnlyList<WindowsShellPropertyValue> ReadDetails(IShellItem shellItem)
	{
		if (shellItem is not IShellItem2 shellItem2
			|| shellItem2.GetPropertyDescriptionList<IPropertyDescriptionList>(PInvoke.PKEY_PropList_FullDetails, out var descriptions).Failed
			|| descriptions is null)
		{
			return [];
		}

		if (shellItem2.GetPropertyStore<IPropertyStore>(GETPROPERTYSTOREFLAGS.GPS_BESTEFFORT, out var propertyStore).Failed || propertyStore is null || descriptions.GetCount(out var count).Failed)
		{
			return [];
		}

		var details = new List<WindowsShellPropertyValue>(checked((int)count));
		for (var index = 0u; index < count; index++)
		{
			if (descriptions.GetAt<IPropertyDescription>(index, out var description).Failed || description is null || description.GetPropertyKey(out var key).Failed)
			{
				continue;
			}

			var name = ReadPropertyDescriptionName(description);
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}

			description.GetTypeFlags(PROPDESC_TYPE_FLAGS.PDTF_MASK_ALL, out var typeFlags);
			var isGroup = typeFlags.HasFlag(PROPDESC_TYPE_FLAGS.PDTF_ISGROUP);
			var value = isGroup ? string.Empty : ReadFormattedProperty(propertyStore, description, key);
			details.Add(new(name, value, isGroup));
		}

		return details;
	}

	private static string ReadPropertyDescriptionName(IPropertyDescription description)
	{
		if (description.GetDisplayName(out var displayName).Succeeded && displayName.Value is not null)
		{
			return ReadAndFree(displayName);
		}

		return description.GetCanonicalName(out var canonicalName).Succeeded && canonicalName.Value is not null ? ReadAndFree(canonicalName) : string.Empty;
	}

	private static string ReadFormattedProperty(IPropertyStore propertyStore, IPropertyDescription description, PROPERTYKEY key)
	{
		if (propertyStore.GetValue(key, out PROPVARIANT value).Failed)
		{
			return string.Empty;
		}

		try
		{
			return description.FormatForDisplay(value, PROPDESC_FORMAT_FLAGS.PDFF_DEFAULT, out var formatted).Succeeded && formatted.Value is not null ? ReadAndFree(formatted) : string.Empty;
		}
		finally
		{
			PInvoke.PropVariantClear(ref value);
		}
	}

	private static string? ReadShellString(IShellItem shellItem, string propertyId)
	{
		if (shellItem is not IShellItem2 shellItem2 || PInvoke.PSGetPropertyKeyFromName(propertyId, out var key).Failed || shellItem2.GetString(key, out var value).Failed)
		{
			return null;
		}

		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static string ReadAndFree(PWSTR value)
	{
		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static string ReadNullTerminated(ReadOnlySpan<char> value)
	{
		var terminator = value.IndexOf('\0');

		return new string(value[..(terminator < 0 ? value.Length : terminator)]);
	}

	private static string NormalizePath(string path)
	{
		return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static bool IsPathWithin(string path, string root)
	{
		return !string.IsNullOrEmpty(root) && (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
	}

	private sealed class SecurityPrincipalBuilder
	{
		internal string Name { get; }

		internal string Sid { get; }

		internal uint AllowedAccessMask { get; set; }

		internal uint DeniedAccessMask { get; set; }

		internal SecurityPrincipalBuilder(string name, string sid)
		{
			Name = name;
			Sid = sid;
		}

		internal WindowsShellSecurityPrincipal Create()
		{
			return new(Name, Sid, AllowedAccessMask, DeniedAccessMask);
		}
	}
}
