using System.Text;

namespace Infrastructure.Email;

// The text/plain alternative: what text-only clients, screen readers in plain-text
// mode and spam filters see. Same order as the HTML part.
internal static class PlainTextEmailWriter
{
	// "-- " (with the trailing space) is the conventional signature separator (RFC 3676),
	// so mail clients can recognise and fold the footer.
	private const string SignatureSeparator = "-- ";

	public static string Write(ComposedEmail email)
	{
		var text = new StringBuilder();

		text.Append(email.Greeting).Append("\n\n");
		text.Append(email.Intro).Append("\n\n");

		if (email.Facts.Count > 0)
		{
			foreach (var fact in email.Facts)
				AppendFact(text, fact);
			text.Append('\n');
		}

		if (email.Lines.Count > 0)
		{
			foreach (var line in email.Lines)
				text.Append("- ").Append(line).Append('\n');
			text.Append('\n');
		}

		if (email.Outro is not null)
			text.Append(email.Outro).Append("\n\n");

		text.Append(email.Action.Label).Append(": ").Append(email.Action.Url).Append("\n\n");
		text.Append(email.Closing).Append("\n\n");

		text.Append(SignatureSeparator).Append('\n');
		text.Append(email.FooterNote).Append('\n');
		text.Append(email.Settings.Label).Append(": ").Append(email.Settings.Url).Append('\n');
		if (email.Unsubscribe is not null)
			text.Append(email.Unsubscribe.Label).Append(": ").Append(email.Unsubscribe.Url).Append('\n');

		return text.ToString();
	}

	private static void AppendFact(StringBuilder text, ComposedFact fact)
	{
		if (fact.Values.Count == 1)
		{
			text.Append(fact.Label).Append(": ").Append(fact.Values[0]).Append('\n');
			return;
		}

		text.Append(fact.Label).Append(":\n");
		foreach (var value in fact.Values)
			text.Append("- ").Append(value).Append('\n');
	}
}
