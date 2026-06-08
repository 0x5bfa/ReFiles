// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Xml.Linq;
using static Files.SourceGenerators.Constants.StringsPropertyGenerator;

namespace Files.SourceGenerators.Parser
{
	/// <summary>
	/// Provides methods to parse RESW (Resource) files and extract keys with optional comments.
	/// </summary>
	internal static class ReswParser
	{
		/// <summary>
		/// Parses a RESW (Resource) file and extracts keys with optional comments.
		/// </summary>
		/// <param name="text">The text in the RESW file to parse.</param>
		/// <returns>An <see cref="IEnumerable{ParserItem}"/> containing the extracted keys and their corresponding values and comments.</returns>
		internal static IEnumerable<ParserItem> GetKeys(string text)
		{
			var document = XDocument.Parse(text);
			var keys = document
				.Descendants()
				.Where(static element => element.Name.LocalName == "data")
				.Select(element => new ParserItem
				{
					Key = element.Attribute("name")?.Value!,
					Value = element.Elements()
						.FirstOrDefault(static child => child.Name.LocalName == "value")
						?.Value ?? string.Empty,
					Comment = element.Elements()
						.FirstOrDefault(static child => child.Name.LocalName == "comment")
						?.Value
				})
				.Where(item => !string.IsNullOrEmpty(item.Key));

			return keys is not null
				? keys.OrderBy(item => item.Key)
				: Enumerable.Empty<ParserItem>();
		}
	}
}
