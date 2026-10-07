namespace Infrastructure.Email;

// A fully worded email in one language, independent of its output format. Both
// PlainTextEmailWriter and HtmlEmailWriter render this same structure, so the two
// parts of one multipart email can never drift apart in content.
internal sealed record ComposedEmail(
	string Language,
	string Subject,
	string Greeting,
	string Intro,
	IReadOnlyList<ComposedFact> Facts,
	IReadOnlyList<string> Lines,
	string? Outro,
	EmailLink Action,
	string Closing,
	string FooterNote,
	EmailLink Settings,
	EmailLink? Unsubscribe);

internal sealed record ComposedFact(string Label, IReadOnlyList<string> Values);

internal sealed record EmailLink(string Label, string Url);
