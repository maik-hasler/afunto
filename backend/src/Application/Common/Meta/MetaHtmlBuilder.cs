namespace Application.Common.Meta;

public static class MetaHtmlBuilder
{
	private const int MaxDescriptionLength = 200;

	public static string Build(
		string title,
		string? description,
		string canonicalUrl,
		string imageUrl)
	{
		var fallbackDescription =
			"Afunto verbindet engagierte Freiwillige mit regionalen Hilfsangeboten.";
		var encodedTitle = HtmlEscape(title);
		var encodedDescription = HtmlEscape(
			Truncate(string.IsNullOrWhiteSpace(description) ? fallbackDescription : description));
		var encodedCanonicalUrl = HtmlEscape(canonicalUrl);
		var encodedImageUrl = HtmlEscape(imageUrl);

		return $"""
			<!doctype html>
			<html lang="de">
				<head>
					<meta charset="UTF-8" />
					<meta name="viewport" content="width=device-width, initial-scale=1.0" />
					<meta name="color-scheme" content="light" />
					<link rel="icon" type="image/svg+xml" href="/favicon.svg" />
					<link rel="apple-touch-icon" href="/icons/icon-192.png" />
					<meta name="theme-color" content="#2d8a5e" />
					<title>{encodedTitle}</title>
					<meta name="description" content="{encodedDescription}" />
					<link rel="canonical" href="{encodedCanonicalUrl}" />
					<meta property="og:type" content="website" />
					<meta property="og:url" content="{encodedCanonicalUrl}" />
					<meta property="og:title" content="{encodedTitle}" />
					<meta property="og:description" content="{encodedDescription}" />
					<meta property="og:image" content="{encodedImageUrl}" />
					<meta name="twitter:card" content="summary_large_image" />
					<meta name="twitter:title" content="{encodedTitle}" />
					<meta name="twitter:description" content="{encodedDescription}" />
					<meta name="twitter:image" content="{encodedImageUrl}" />
				</head>
				<body>
					<p><a href="{encodedCanonicalUrl}">{encodedTitle}</a></p>
					<p>{encodedDescription}</p>
				</body>
			</html>
			""";
	}

	private static string Truncate(string value)
	{
		if (value.Length <= MaxDescriptionLength)
			return value;

		var cutIndex = char.IsLowSurrogate(value[MaxDescriptionLength])
			? MaxDescriptionLength - 1
			: MaxDescriptionLength;

		return string.Concat(value.AsSpan(0, cutIndex).TrimEnd(), "...");
	}

	private static string HtmlEscape(string value) =>
		value
			.Replace("&", "&amp;")
			.Replace("<", "&lt;")
			.Replace(">", "&gt;")
			.Replace("\"", "&quot;")
			.Replace("'", "&#39;");
}
