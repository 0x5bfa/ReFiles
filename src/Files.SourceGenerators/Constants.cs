// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.SourceGenerators
{
	/// <summary>
	/// Contains various constants used within the source generator.
	/// </summary>
	internal class Constants
	{
		/// <summary>
		/// Contains diagnostic descriptors used for error reporting.
		/// </summary>
		internal class DiagnosticDescriptors
		{
			/// <summary>
			/// Diagnostic descriptor for a scenario where multiple files with the same name are detected.
			/// </summary>
			internal static readonly DiagnosticDescriptor FSG1003 = new(
				id: nameof(FSG1003),
				title: "Multiple files with the same name detected",
				messageFormat: "Multiple files named '{0}' were detected. Ensure all generated localization string files have unique names.",
				category: "FileGeneration",
				defaultSeverity: DiagnosticSeverity.Error,
				isEnabledByDefault: true,
				description: "This diagnostic detects cases where multiple localization string files are being generated with the same name," +
				"which can cause conflicts and overwrite issues.");

			/// <summary>
			/// Diagnostic descriptor for malformed resource files.
			/// </summary>
			internal static readonly DiagnosticDescriptor FSG1004 = new(
				id: nameof(FSG1004),
				title: "Resource file could not be parsed",
				messageFormat: "Resource file '{0}' could not be parsed: {1}",
				category: "FileGeneration",
				defaultSeverity: DiagnosticSeverity.Error,
				isEnabledByDefault: true);

		}

		internal class StringsPropertyGenerator
		{
			/// <summary>
			/// The name of the generated class that contains string constants.
			/// </summary>
			internal const string StringsClassName = "Strings";

			/// <summary>
			/// The namespace of the generated localization types.
			/// </summary>
			internal const string StringsNamespace = "Files.Localization";

		}
	}
}
