namespace Application.Common.Email;

// What a handler knows about one email, before any wording: the renderer owns the copy,
// the layout and both output formats (HTML and plain text), so every email shares one
// shape and no handler concatenates user-facing strings.
public sealed record EmailDraft(
	EmailTemplateKind Kind,
	string Language,
	string RecipientName,
	string ActionUrl)
{
	public IReadOnlyDictionary<string, string> Placeholders { get; init; } = new Dictionary<string, string>();

	// Selects the template's plural wording when greater than 1, e.g. one confirmation
	// email covering several dates of the same opportunity.
	public int Count { get; init; } = 1;

	public IReadOnlyList<EmailFact> Facts { get; init; } = [];

	public IReadOnlyList<EmailLine> Lines { get; init; } = [];

	// Set only for email types the recipient can opt out of; null renders no unsubscribe link.
	public string? UnsubscribeUrl { get; init; }
}

// A labelled block under the intro ("Termin: ...", "Ort: ..."). The renderer supplies the
// localized label and formats dates in the recipient's language.
public abstract record EmailFact
{
	private EmailFact()
	{
	}

	public sealed record Schedule(IReadOnlyList<EmailTimeRange> Slots) : EmailFact;

	// A null address means the opportunity takes place online.
	public sealed record Location(string? Address) : EmailFact;

	public sealed record Reason(string Text) : EmailFact;
}

public sealed record EmailTimeRange(DateTimeOffset Start, DateTimeOffset End);

// One bullet in a list email (the organizer digest). Key names the template's line format.
public sealed record EmailLine(string Key, IReadOnlyDictionary<string, string> Placeholders);

public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody, string? UnsubscribeUrl);
