// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using static Files.SourceGenerators.Constants.DiagnosticDescriptors;
using static Files.SourceGenerators.Constants.StringsPropertyGenerator;
using static Files.SourceGenerators.Utilities.SourceGeneratorHelper;

namespace Files.SourceGenerators.Generators
{
	/// <summary>
	/// Generates resource key constants and localization helpers.
	/// </summary>
	[Generator]
	internal sealed class StringsPropertyGenerator : IIncrementalGenerator
	{
		/// <summary>
		/// Initializes the generator and registers source output based on English resource files.
		/// </summary>
		/// <param name="context">The initialization context.</param>
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var additionalFiles = context
				.AdditionalTextsProvider
				.Where(static file => IsEnglishResourceFile(file.Path));

			context.RegisterSourceOutput(additionalFiles, Execute);

			var additionalFilePaths = additionalFiles
				.Select(static (file, _) => file.Path)
				.Collect();

			context.RegisterSourceOutput(additionalFilePaths, ExecuteLocalizationExtensions);

			var additionalFileNames = additionalFiles
				.Select(static (file, _) => SystemIO.Path.GetFileNameWithoutExtension(file.Path))
				.Collect();

			context.RegisterSourceOutput(additionalFileNames, static (ctx, fileNames) =>
			{
				if (fileNames.Length <= 1)
				{
					return;
				}

				var duplicates = fileNames
					.GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
					.Where(static group => group.Count() > 1)
					.Select(static group => group.Key);

				foreach (string fileName in duplicates)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(FSG1003, Location.None, fileName));
				}
			});
		}

		private static bool IsEnglishResourceFile(string path)
		{
			var normalizedPath = path.Replace('\\', '/');
			return normalizedPath.EndsWith("en-US/Resources.resw", StringComparison.OrdinalIgnoreCase)
				|| normalizedPath.EndsWith("en-US/Resources.json", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Generates the constants for a resource file.
		/// </summary>
		/// <param name="ctx">The source production context.</param>
		/// <param name="file">The additional text file.</param>
		private static void Execute(SourceProductionContext ctx, AdditionalText file)
		{
			var fileName = SystemIO.Path.GetFileNameWithoutExtension(file.Path);
			IReadOnlyList<ParserItem> keys;
			try
			{
				keys = ReadAllKeys(file, ctx.CancellationToken).ToArray();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception)
			{
				ctx.ReportDiagnostic(Diagnostic.Create(FSG1004, Location.None, file.Path, exception.Message));
				return;
			}

			var usedNames = new HashSet<string>(StringComparer.Ordinal);
			var tabString = Spacing(1);

			var sb = new StringBuilder(8000);
			_ = sb.AppendFullHeader(file.Path);
			_ = sb.AppendLine();
			_ = sb.AppendLine($"namespace {StringsNamespace}");
			_ = sb.AppendLine("{");
			_ = sb.AppendLine($"{tabString}/// <summary>");
			_ = sb.AppendLine($"{tabString}/// Represents the keys of the application's string resources.");
			_ = sb.AppendLine($"{tabString}/// </summary>");
			_ = sb.AppendLine($"{tabString}public static partial class {StringsClassName}");
			_ = sb.AppendLine($"{tabString}{{");

			foreach (var key in keys)
			{
				var constantName = GetUniqueConstantName(key.Key, usedNames);
				AddKey(buffer: sb, constantName: constantName, resourceKey: key.Key, comment: key.Comment, exampleValue: key.Value);
			}

			_ = sb.AppendLine($"{tabString}}}");
			_ = sb.AppendLine("}");

			ctx.AddSource($"{StringsClassName}.{fileName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		private static void ExecuteLocalizationExtensions(SourceProductionContext ctx, ImmutableArray<string> filePaths)
		{
			if (filePaths.IsDefaultOrEmpty)
			{
				return;
			}

			var sourcePath = filePaths
				.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
				.First();
			var tabString = Spacing(1);
			var sb = new StringBuilder(2400);
			_ = sb.AppendFullHeader(sourcePath);
			_ = sb.AppendLine();
			_ = sb.AppendLine("#nullable enable");
			_ = sb.AppendLine();
			_ = sb.AppendLine($"namespace {StringsNamespace}");
			_ = sb.AppendLine("{");
			_ = sb.AppendLine($"{tabString}/// <summary>");
			_ = sb.AppendLine(
				$"{tabString}/// Provides localized values for resource keys.");
			_ = sb.AppendLine($"{tabString}/// </summary>");
			_ = sb.AppendLine($"{tabString}public static class LocalizationExtensions");
			_ = sb.AppendLine($"{tabString}{{");
			_ = sb.AppendLine(
				$"{tabString}{Spacing(1)}private static readonly " +
				"global::Microsoft.Windows.ApplicationModel.Resources.ResourceMap? Resources = " +
				"new global::Microsoft.Windows.ApplicationModel.Resources.ResourceManager()");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}.MainResourceMap");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}.TryGetSubtree(\"Resources\");");
			_ = sb.AppendLine(
				$"{tabString}{Spacing(1)}private static readonly " +
				"global::System.Collections.Concurrent.ConcurrentDictionary<string, string> " +
				"LocalizedResources = new(global::System.StringComparer.Ordinal);");
			_ = sb.AppendLine();
			_ = sb.AppendLine($"{tabString}{Spacing(1)}public static string GetLocalized(this string resourceKey)");
			_ = sb.AppendLine($"{tabString}{Spacing(1)}{{");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}global::System.ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);");
			_ = sb.AppendLine();
			_ = sb.AppendLine($"{tabString}{Spacing(2)}if (LocalizedResources.TryGetValue(resourceKey, out var value))");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}{{");
			_ = sb.AppendLine($"{tabString}{Spacing(3)}return value;");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}}}");
			_ = sb.AppendLine();
			_ = sb.AppendLine($"{tabString}{Spacing(2)}value = Resources?.TryGetValue(resourceKey)?.ValueAsString ?? resourceKey;");
			_ = sb.AppendLine($"{tabString}{Spacing(2)}return LocalizedResources.GetOrAdd(resourceKey, value);");
			_ = sb.AppendLine($"{tabString}{Spacing(1)}}}");
			_ = sb.AppendLine();
			_ = sb.AppendLine($"{tabString}{Spacing(1)}public static void ClearLocalizedCache() => LocalizedResources.Clear();");
			_ = sb.AppendLine($"{tabString}}}");
			_ = sb.AppendLine("}");

			ctx.AddSource("LocalizationExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		/// <summary>
		/// Adds a resource key constant to the generated source.
		/// </summary>
		private static void AddKey(StringBuilder buffer, string constantName, string resourceKey, string? comment, string? exampleValue, int tabPos = 2)
		{
			var tabString = Spacing(tabPos);
			if (comment is not null || exampleValue is not null)
			{
				_ = buffer.AppendLine();
				_ = buffer.AppendLine($"{tabString}/// <summary>");

				if (comment is not null)
				{
					_ = buffer.AppendLine(
						$"{tabString}/// {EscapeDocumentation(comment)}");
				}

				_ = buffer.AppendLine($"{tabString}/// </summary>");

				if (exampleValue is not null)
				{
					_ = buffer.AppendLine($"{tabString}/// <remarks>");
					_ = buffer.AppendLine(
						$"{tabString}/// e.g.: <b>{EscapeDocumentation(exampleValue)}</b>");
					_ = buffer.AppendLine($"{tabString}/// </remarks>");
				}
			}

			_ = buffer.AppendLine($"{tabString}public const string {constantName} = " + $"\"{EscapeStringLiteral(resourceKey)}\";");
		}

		private static string GetUniqueConstantName(string resourceKey, HashSet<string> usedNames)
		{
			var baseName = KeyNameValidator(resourceKey);
			if (usedNames.Add(baseName))
			{
				return baseName;
			}

			for (var suffix = 2; ; suffix++)
			{
				var candidate = $"{baseName}_{suffix}";
				if (usedNames.Add(candidate))
				{
					return candidate;
				}
			}
		}

		/// <summary>
		/// Reads all keys from the provided file based on its extension.
		/// </summary>
		private static IEnumerable<ParserItem> ReadAllKeys(AdditionalText file, CancellationToken cancellationToken)
		{
			var text = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
			return SystemIO.Path.GetExtension(file.Path) switch
			{
				var extension when string.Equals(extension, ".resw", StringComparison.OrdinalIgnoreCase) => ReswParser.GetKeys(text),
				var extension when string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) => JsonParser.GetKeys(text),
				_ => []
			};
		}

		private static string KeyNameValidator(string key)
		{
			var builder = new StringBuilder(key.Length + 1);
			foreach (var character in key)
			{
				if (builder.Length is 0)
				{
					if (SyntaxFacts.IsIdentifierStartCharacter(character))
					{
						_ = builder.Append(character);
					}
					else if (SyntaxFacts.IsIdentifierPartCharacter(character))
					{
						_ = builder.Append('_').Append(character);
					}
					else
					{
						_ = builder.Append('_');
					}

					continue;
				}

				_ = builder.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
			}

			if (builder.Length is 0)
			{
				_ = builder.Append("Resource");
			}

			var result = builder.ToString();
			return SyntaxFacts.GetKeywordKind(result) is not SyntaxKind.None
				|| SyntaxFacts.GetContextualKeywordKind(result) is not SyntaxKind.None
				? $"_{result}"
				: result;
		}

		private static string EscapeDocumentation(string value) =>
			value
				.Replace("&", "&amp;")
				.Replace("<", "&lt;")
				.Replace(">", "&gt;")
				.Replace('\r', ' ')
				.Replace('\n', ' ');

		private static string EscapeStringLiteral(string value) =>
			value
				.Replace("\\", "\\\\")
				.Replace("\"", "\\\"")
				.Replace("\r", "\\r")
				.Replace("\n", "\\n")
				.Replace("\t", "\\t");
	}
}
