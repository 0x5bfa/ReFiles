// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.SourceGenerators.Generators;
using Files.SourceGenerators.Parser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Files.UnitTests;

/// <summary>
/// Verifies deterministic and conflict-free localization source generation.
/// </summary>
[TestClass]
public sealed class StringsPropertyGeneratorTests
{
	/// <summary>
	/// Verifies that duplicate English resource files produce a diagnostic instead of conflicting generated sources.
	/// </summary>
	[TestMethod]
	public void DuplicateEnglishResourcesReportDiagnosticWithoutGeneratorFailure()
	{
		var result = RunGenerator(
			new InMemoryAdditionalText(@"C:\first\en-US\Resources.resw", CreateResource("First", "One")),
			new InMemoryAdditionalText(@"D:\second\en-US\Resources.resw", CreateResource("Second", "Two")));
		Assert.AreEqual(1, result.Results.Length);
		var generatorResult = result.Results[0];

		Assert.IsNull(generatorResult.Exception);
		Assert.AreEqual(0, generatorResult.GeneratedSources.Length);
		Assert.AreEqual(1, result.Diagnostics.Count(static diagnostic => diagnostic.Id is "FSG1003"));
	}

	/// <summary>
	/// Verifies that generated output does not contain the checkout-specific absolute path.
	/// </summary>
	[TestMethod]
	public void CheckoutLocationDoesNotChangeGeneratedOutput()
	{
		var resource = CreateResource("Greeting", "Hello");
		var firstResult = GetGeneratedSources(RunGenerator(new InMemoryAdditionalText(@"C:\repo-one\src\Files\Strings\en-US\Resources.resw", resource)));
		var secondResult = GetGeneratedSources(RunGenerator(new InMemoryAdditionalText(@"D:\repo-two\src\Files\Strings\en-US\Resources.resw", resource)));

		CollectionAssert.AreEqual(firstResult, secondResult);
		Assert.IsFalse(firstResult.Any(static source => source.Contains("repo-one", StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>
	/// Verifies that a directory whose name merely ends with en-US is not treated as the English resource directory.
	/// </summary>
	[TestMethod]
	public void SimilarDirectoryNameIsNotTreatedAsEnglishResourceDirectory()
	{
		var result = RunGenerator(new InMemoryAdditionalText(@"C:\repo\not-en-US\Resources.resw", CreateResource("Greeting", "Hello")));
		Assert.AreEqual(1, result.Results.Length);
		var generatorResult = result.Results[0];

		Assert.IsNull(generatorResult.Exception);
		Assert.AreEqual(0, generatorResult.GeneratedSources.Length);
	}

	/// <summary>
	/// Verifies that resource keys are sorted using ordinal comparison.
	/// </summary>
	[TestMethod]
	public void ResourceKeysUseOrdinalOrdering()
	{
		var keys = ReswParser.GetKeys(CreateResource(("ä", "Third"), ("a", "Second"), ("Z", "First"))).Select(static item => item.Key).ToArray();

		CollectionAssert.AreEqual(new[] { "Z", "a", "ä" }, keys);
	}

	private static GeneratorDriverRunResult RunGenerator(params AdditionalText[] additionalTexts)
	{
		var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
		var syntaxTree = CSharpSyntaxTree.ParseText("internal sealed class Placeholder { }", parseOptions);
		var compilation = CSharpCompilation.Create(
			assemblyName: "GeneratorTests",
			syntaxTrees: new[] { syntaxTree },
			references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			generators: new[] { new StringsPropertyGenerator().AsSourceGenerator() },
			additionalTexts: additionalTexts,
			parseOptions: parseOptions);

		driver = driver.RunGenerators(compilation);

		return driver.GetRunResult();
	}

	private static string[] GetGeneratedSources(GeneratorDriverRunResult result)
	{
		Assert.AreEqual(1, result.Results.Length);
		var generatorResult = result.Results[0];
		Assert.IsNull(generatorResult.Exception);

		return generatorResult.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).Select(static source => source.SourceText.ToString()).ToArray();
	}

	private static string CreateResource(string key, string value) => CreateResource((key, value));

	private static string CreateResource(params (string Key, string Value)[] resources)
	{
		var data = string.Join(string.Empty, resources.Select(static resource => $"<data name=\"{resource.Key}\"><value>{resource.Value}</value></data>"));

		return $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root>{data}</root>";
	}

	private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
	}
}
