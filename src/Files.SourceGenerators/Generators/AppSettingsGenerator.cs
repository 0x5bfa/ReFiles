// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Files.SourceGenerators.Constants.DiagnosticDescriptors;

namespace Files.SourceGenerators.Generators
{
	/// <summary>
	/// Generates the application settings facade and validation code.
	/// </summary>
	[Generator]
	internal sealed class AppSettingsGenerator : IIncrementalGenerator
	{
		private const string GeneratedSettingsPropertyAttributeMetadataName = "Files.Settings.GeneratedSettingsPropertyAttribute";
		private const string AppSettingsDataMetadataName = "Files.Settings.AppSettingsData";
		private const string AppSettingsServiceMetadataName = "Files.Settings.AppSettingsService";

		/// <summary>
		/// Initializes the generator and registers source output for application settings.
		/// </summary>
		/// <param name="context">The initialization context.</param>
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var settings = context.SyntaxProvider.CreateSyntaxProvider(
				static (node, _) => node is PropertyDeclarationSyntax { AttributeLists.Count: > 0 },
				static (syntaxContext, cancellationToken) => GetSetting(syntaxContext, cancellationToken))
				.Where(static setting => setting is not null)
				.Select(static (setting, _) => setting!);

			context.RegisterSourceOutput(settings.Collect(), Execute);
		}

		private static SettingModel? GetSetting(GeneratorSyntaxContext context, CancellationToken cancellationToken)
		{
			if (context.Node is not PropertyDeclarationSyntax propertySyntax)
			{
				return null;
			}

			if (context.SemanticModel.GetDeclaredSymbol(propertySyntax, cancellationToken) is not IPropertySymbol property)
			{
				return null;
			}

			if (!string.Equals(property.ContainingType.ToDisplayString(), AppSettingsServiceMetadataName, StringComparison.Ordinal))
			{
				return null;
			}

			var attribute = property.GetAttributes().FirstOrDefault(static attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), GeneratedSettingsPropertyAttributeMetadataName, StringComparison.Ordinal));
			if (attribute is null)
			{
				return null;
			}

			var location = propertySyntax.Identifier.GetLocation();
			if (!propertySyntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
			{
				return SettingModel.Invalid(property.Name, location, "The property must be declared as partial.");
			}

			if (property.DeclaredAccessibility is not Accessibility.Public || property.GetMethod is null || property.SetMethod is null)
			{
				return SettingModel.Invalid(property.Name, location, "The property must be a public read-write property.");
			}

			if (attribute.ConstructorArguments.Length != 1)
			{
				return SettingModel.Invalid(property.Name, location, "The setting must specify exactly one default value.");
			}

			var kind = GetSettingKind(property.Type);
			if (kind is SettingKind.Unsupported)
			{
				return SettingModel.Invalid(property.Name, location, $"The type '{property.Type.ToDisplayString()}' is not supported.");
			}

			var defaultValue = attribute.ConstructorArguments[0];
			if (!TryFormatValue(defaultValue, property.Type, out var defaultValueExpression))
			{
				return SettingModel.Invalid(property.Name, location, "The default value is not compatible with the property type.");
			}

			var minValueExpression = GetConstraintValue(attribute, "MinValue", property.Type, out var minValue, out var minValueError);
			if (minValueError is not null)
			{
				return SettingModel.Invalid(property.Name, location, minValueError);
			}

			var maxValueExpression = GetConstraintValue(attribute, "MaxValue", property.Type, out var maxValue, out var maxValueError);
			if (maxValueError is not null)
			{
				return SettingModel.Invalid(property.Name, location, maxValueError);
			}

			if ((minValueExpression is not null || maxValueExpression is not null) && !IsNumeric(kind))
			{
				return SettingModel.Invalid(property.Name, location, "MinValue and MaxValue can only be used with numeric settings.");
			}

			if (minValue is not null && maxValue is not null && CompareNumericValues(minValue, maxValue) > 0)
			{
				return SettingModel.Invalid(property.Name, location, "MinValue cannot be greater than MaxValue.");
			}

			var dataType = context.SemanticModel.Compilation.GetTypeByMetadataName(AppSettingsDataMetadataName);
			var dataProperty = dataType?.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault();
			if (dataProperty is null || !SymbolEqualityComparer.Default.Equals(dataProperty.Type, property.Type))
			{
				return SettingModel.Invalid(property.Name, location, "The matching AppSettingsData property is missing or has a different type.");
			}

			return new SettingModel(
				property.Name,
				property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				kind,
				defaultValueExpression,
				minValueExpression,
				maxValueExpression,
				dataProperty.NullableAnnotation is NullableAnnotation.Annotated,
				location);
		}

		private static void Execute(SourceProductionContext context, ImmutableArray<SettingModel> settings)
		{
			if (settings.IsDefaultOrEmpty)
			{
				return;
			}

			var invalidSettings = settings.Where(static setting => setting.ErrorMessage is not null).ToArray();
			foreach (var setting in invalidSettings)
			{
				context.ReportDiagnostic(Diagnostic.Create(FSG1005, setting.Location, setting.Name, setting.ErrorMessage));
			}

			if (invalidSettings.Length > 0)
			{
				return;
			}

			var duplicateSettings = settings
				.GroupBy(static setting => setting.Name, StringComparer.Ordinal)
				.Where(static group => group.Count() > 1)
				.Select(static group => group.Key)
				.ToArray();
			if (duplicateSettings.Length > 0)
			{
				foreach (var duplicateSetting in duplicateSettings)
				{
					var setting = settings.First(setting => string.Equals(setting.Name, duplicateSetting, StringComparison.Ordinal));
					context.ReportDiagnostic(Diagnostic.Create(FSG1006, setting.Location, duplicateSetting));
				}

				return;
			}

			var orderedSettings = settings.OrderBy(static setting => setting.Name, StringComparer.Ordinal).ToArray();
			var buffer = new StringBuilder(12000);
			_ = buffer.AppendFullHeader();
			_ = buffer.AppendLine("#nullable enable");
			_ = buffer.AppendLine();
			_ = buffer.AppendLine("namespace Files.Settings");
			_ = buffer.AppendLine("{");
			_ = buffer.AppendLine();
			_ = buffer.AppendLine("\tinternal sealed partial class AppSettingsService");
			_ = buffer.AppendLine("\t{");

			foreach (var setting in orderedSettings)
			{
				AppendServiceProperty(buffer, setting);
			}

			AppendNormalizeSettings(buffer, orderedSettings);
			foreach (var setting in orderedSettings.Where(static setting => setting.RequiresNormalizer))
			{
				AppendNormalizer(buffer, setting);
			}

			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine();
			_ = buffer.AppendLine("\tinternal sealed partial class AppSettingsData");
			_ = buffer.AppendLine("\t{");
			AppendDataConstructor(buffer, orderedSettings);
			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine("}");

			context.AddSource("AppSettings.g.cs", SourceText.From(buffer.ToString(), Encoding.UTF8));
		}

		private static void AppendServiceProperty(StringBuilder buffer, SettingModel setting)
		{
			var dataAccess = $"_settings.{setting.Name}";
			var getterExpression = setting.IsNullableReference ? $"{dataAccess} ?? {setting.DefaultValueExpression}" : dataAccess;
			var dataGetter = setting.IsNullableReference ? $"settings.{setting.Name} ?? {setting.DefaultValueExpression}" : $"settings.{setting.Name}";
			var normalizedValue = GetNormalizedValueExpression(setting, "value");

			_ = buffer.AppendLine($"\tpublic partial {setting.TypeName} {setting.Name}");
			_ = buffer.AppendLine("\t{");
			_ = buffer.AppendLine("\t\tget");
			_ = buffer.AppendLine("\t\t{");
			_ = buffer.AppendLine("\t\t\tlock (_syncRoot)");
			_ = buffer.AppendLine("\t\t\t{");
			_ = buffer.AppendLine($"\t\t\t\treturn {getterExpression};");
			_ = buffer.AppendLine("\t\t\t}");
			_ = buffer.AppendLine("\t\t}");
			_ = buffer.AppendLine($"\t\tset => SetValue(nameof({setting.Name}), {normalizedValue}, static settings => {dataGetter}, static (settings, nextValue) => settings.{setting.Name} = nextValue);");
			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine();
		}

		private static void AppendNormalizeSettings(StringBuilder buffer, IReadOnlyList<SettingModel> settings)
		{
			_ = buffer.AppendLine("\tprivate static void NormalizeSettings(AppSettingsData settings)");
			_ = buffer.AppendLine("\t{");
			foreach (var setting in settings)
			{
				if (setting.IsNullableReference)
				{
					_ = buffer.AppendLine($"\t\tsettings.{setting.Name} ??= {setting.DefaultValueExpression};");
				}
				else if (setting.RequiresNormalizer)
				{
					_ = buffer.AppendLine($"\t\tsettings.{setting.Name} = Normalize{setting.Name}(settings.{setting.Name});");
				}
			}

			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine();
		}

		private static void AppendNormalizer(StringBuilder buffer, SettingModel setting)
		{
			_ = buffer.AppendLine($"\tprivate static {setting.TypeName} Normalize{setting.Name}({setting.TypeName} value)");
			_ = buffer.AppendLine("\t{");
			var conditions = new List<string>();
			if (setting.Kind is SettingKind.FloatingPoint)
			{
				var floatingType = setting.TypeName == "global::System.Single" ? "Single" : "Double";
				conditions.Add($"!global::System.{floatingType}.IsFinite(value)");
			}

			if (setting.MinValueExpression is not null)
			{
				conditions.Add($"value < {setting.MinValueExpression}");
			}

			if (setting.MaxValueExpression is not null)
			{
				conditions.Add($"value > {setting.MaxValueExpression}");
			}

			if (setting.Kind is SettingKind.Enum)
			{
				conditions.Add("!global::System.Enum.IsDefined(value)");
			}

			_ = buffer.AppendLine($"\t\tif ({string.Join(" || ", conditions)})");
			_ = buffer.AppendLine("\t\t{");
			_ = buffer.AppendLine($"\t\t\treturn {setting.DefaultValueExpression};");
			_ = buffer.AppendLine("\t\t}");
			_ = buffer.AppendLine();
			_ = buffer.AppendLine("\t\treturn value;");
			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine();
		}

		private static void AppendDataConstructor(StringBuilder buffer, IReadOnlyList<SettingModel> settings)
		{
			_ = buffer.AppendLine("\tinternal AppSettingsData()");
			_ = buffer.AppendLine("\t{");
			foreach (var setting in settings)
			{
				_ = buffer.AppendLine($"\t\t{setting.Name} = {setting.DefaultValueExpression};");
			}

			_ = buffer.AppendLine("\t}");
			_ = buffer.AppendLine();
		}

		private static string GetNormalizedValueExpression(SettingModel setting, string valueExpression)
		{
			if (setting.IsNullableReference)
			{
				return $"{valueExpression} ?? {setting.DefaultValueExpression}";
			}

			return setting.RequiresNormalizer ? $"Normalize{setting.Name}({valueExpression})" : valueExpression;
		}

		private static SettingKind GetSettingKind(ITypeSymbol type)
		{
			if (type.TypeKind is TypeKind.Enum)
			{
				return SettingKind.Enum;
			}

			return type.SpecialType switch
			{
				SpecialType.System_String => SettingKind.String,
				SpecialType.System_Boolean => SettingKind.Boolean,
				SpecialType.System_Byte
				or SpecialType.System_SByte
				or SpecialType.System_Int16
				or SpecialType.System_UInt16
				or SpecialType.System_Int32
				or SpecialType.System_UInt32
				or SpecialType.System_Int64
				or SpecialType.System_UInt64 => SettingKind.Integer,
				SpecialType.System_Single or SpecialType.System_Double => SettingKind.FloatingPoint,
				_ => SettingKind.Unsupported,
			};
		}

		private static bool IsNumeric(SettingKind kind) => kind is SettingKind.Integer or SettingKind.FloatingPoint;

		private static string? GetConstraintValue(AttributeData attribute, string name, ITypeSymbol targetType, out object? rawValue, out string? errorMessage)
		{
			rawValue = null;
			errorMessage = null;
			if (!TryGetNamedArgument(attribute, name, out var value) || value.IsNull)
			{
				return null;
			}

			if (!TryFormatValue(value, targetType, out var expression))
			{
				errorMessage = $"{name} must be a numeric value compatible with the property type.";

				return null;
			}

			rawValue = value.Value;
			if (rawValue is float floatValue && (float.IsNaN(floatValue) || float.IsInfinity(floatValue)) || rawValue is double doubleValue && (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)))
			{
				rawValue = null;
				errorMessage = $"{name} must be finite.";

				return null;
			}

			return expression;
		}

		private static bool TryGetNamedArgument(AttributeData attribute, string name, out TypedConstant value)
		{
			foreach (var namedArgument in attribute.NamedArguments)
			{
				if (string.Equals(namedArgument.Key, name, StringComparison.Ordinal))
				{
					value = namedArgument.Value;

					return true;
				}
			}

			value = default;

			return false;
		}

		private static bool TryFormatValue(TypedConstant value, ITypeSymbol targetType, out string expression)
		{
			expression = string.Empty;
			if (value.IsNull)
			{
				expression = "null";

				return targetType.NullableAnnotation is NullableAnnotation.Annotated;
			}

			if (targetType.TypeKind is TypeKind.Enum)
			{
				if (value.Kind is not TypedConstantKind.Enum)
				{
					return false;
				}

				return TryFormatEnumValue(value, (INamedTypeSymbol)targetType, out expression);
			}

			if (targetType.SpecialType is SpecialType.System_String)
			{
				if (value.Value is not string stringValue)
				{
					return false;
				}

				expression = FormatStringLiteral(stringValue);

				return true;
			}

			if (targetType.SpecialType is SpecialType.System_Boolean)
			{
				if (value.Value is not bool boolValue)
				{
					return false;
				}

				expression = boolValue ? "true" : "false";

				return true;
			}

			if (!IsNumericSpecialType(targetType.SpecialType) || !IsNumericValue(value.Value) || !IsFiniteNumericValue(value.Value!))
			{
				return false;
			}

			expression = FormatNumericLiteral(value.Value!, targetType.SpecialType);

			return true;
		}

		private static bool TryFormatEnumValue(TypedConstant value, INamedTypeSymbol enumType, out string expression)
		{
			var field = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value.Value));
			var enumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (field is not null)
			{
				expression = $"{enumTypeName}.{field.Name}";

				return true;
			}

			if (!IsNumericValue(value.Value))
			{
				expression = string.Empty;

				return false;
			}

			expression = $"({enumTypeName}){FormatNumericLiteral(value.Value!, enumType.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32)}";

			return true;
		}

		private static bool IsNumericSpecialType(SpecialType specialType) => IsIntegerSpecialType(specialType) || specialType is SpecialType.System_Single or SpecialType.System_Double;

		private static bool IsIntegerSpecialType(SpecialType specialType) => specialType is
			SpecialType.System_Byte
			or SpecialType.System_SByte
			or SpecialType.System_Int16
			or SpecialType.System_UInt16
			or SpecialType.System_Int32
			or SpecialType.System_UInt32
			or SpecialType.System_Int64
			or SpecialType.System_UInt64;

		private static bool IsNumericValue(object? value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double;

		private static bool IsFiniteNumericValue(object value)
		{
			if (value is float floatValue)
			{
				return !float.IsNaN(floatValue) && !float.IsInfinity(floatValue);
			}

			if (value is double doubleValue)
			{
				return !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue);
			}

			return true;
		}

		private static int CompareNumericValues(object left, object right)
		{
			if (left is float or double || right is float or double)
			{
				return Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
			}

			return Convert.ToDecimal(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
		}

		private static string FormatNumericLiteral(object value, SpecialType targetType)
		{
			return targetType switch
			{
				SpecialType.System_Single => $"{Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)}F",
				SpecialType.System_Double => $"{Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)}D",
				SpecialType.System_UInt64 => $"{Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}UL",
				SpecialType.System_Int64 => $"{Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}L",
				SpecialType.System_UInt32 => $"{Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}U",
				_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
			};
		}

		private static string FormatStringLiteral(string value)
		{
			var buffer = new StringBuilder(value.Length + 2);
			_ = buffer.Append('"');
			foreach (var character in value)
			{
				switch (character)
				{
					case '\\':
						_ = buffer.Append("\\\\");
						break;
					case '"':
						_ = buffer.Append("\\\"");
						break;
					case '\r':
						_ = buffer.Append("\\r");
						break;
					case '\n':
						_ = buffer.Append("\\n");
						break;
					case '\t':
						_ = buffer.Append("\\t");
						break;
					default:
						if (character < ' ')
						{
							_ = buffer.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
						}
						else
						{
							_ = buffer.Append(character);
						}

						break;
				}
			}

			_ = buffer.Append('"');

			return buffer.ToString();
		}

		private enum SettingKind
		{
			Unsupported,
			String,
			Boolean,
			Integer,
			FloatingPoint,
			Enum,
		}

		private sealed class SettingModel
		{
			private SettingModel(string name, Location location, string? errorMessage)
			{
				Name = name;
				TypeName = string.Empty;
				Kind = SettingKind.Unsupported;
				DefaultValueExpression = string.Empty;
				Location = location;
				ErrorMessage = errorMessage;
			}

			internal SettingModel(
				string name,
				string typeName,
				SettingKind kind,
				string defaultValueExpression,
				string? minValueExpression,
				string? maxValueExpression,
				bool isNullableReference,
				Location location)
				: this(name, location, null)
			{
				TypeName = typeName;
				Kind = kind;
				DefaultValueExpression = defaultValueExpression;
				MinValueExpression = minValueExpression;
				MaxValueExpression = maxValueExpression;
				IsNullableReference = isNullableReference;
			}

			internal string Name { get; }

			internal string TypeName { get; }

			internal SettingKind Kind { get; }

			internal string DefaultValueExpression { get; }

			internal string? MinValueExpression { get; }

			internal string? MaxValueExpression { get; }

			internal bool IsNullableReference { get; }

			internal Location Location { get; }

			internal string? ErrorMessage { get; }

			internal bool RequiresNormalizer => Kind is SettingKind.Enum or SettingKind.FloatingPoint || MinValueExpression is not null || MaxValueExpression is not null;

			internal static SettingModel Invalid(string name, Location location, string errorMessage) => new(name, location, errorMessage);
		}
	}
}
