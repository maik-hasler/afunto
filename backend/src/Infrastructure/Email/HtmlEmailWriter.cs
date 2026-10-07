using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Infrastructure.Email;

// The text/html alternative. Deliberately plain: one centred card, inline styles and
// layout tables only, because many mail clients drop <style> blocks and ignore modern
// CSS. Every piece of text is HTML-encoded here - opportunity titles, names and reasons
// are user input and must never reach the markup unescaped.
internal static class HtmlEmailWriter
{
	// Brand tokens from the frontend (brand-50/700/800). White on brand-700 passes WCAG AA.
	private const string PageBackground = "#f0faf5";
	private const string CardBackground = "#ffffff";
	private const string Brand = "#226947";
	private const string BrandDark = "#1a3c2b";
	private const string TextColor = "#111827";
	private const string MutedColor = "#6b7280";
	private const string FontStack = "'Source Sans 3', 'Segoe UI', Arial, Helvetica, sans-serif";

	private const string ParagraphStyle = "margin:0 0 16px;";

	// Escapes only what is markup-relevant (<, >, &, quotes) and leaves umlauts and German
	// quotation marks readable - the part is sent as UTF-8, so they need no entities.
	private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

	public static string Write(ComposedEmail email)
	{
		var html = new StringBuilder();

		html.Append("<!DOCTYPE html>\n");
		html.Append("<html lang=\"").Append(Encode(email.Language)).Append("\">\n");
		html.Append("<head>\n");
		html.Append("<meta charset=\"utf-8\">\n");
		html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
		html.Append("<title>").Append(Encode(email.Subject)).Append("</title>\n");
		html.Append("</head>\n");
		html.Append("<body style=\"margin:0;padding:0;background-color:").Append(PageBackground).Append(";\">\n");

		// Preheader: the inbox preview line next to the subject, hidden in the opened mail.
		html.Append("<div style=\"display:none;max-height:0;overflow:hidden;\">").Append(Encode(email.Intro)).Append("</div>\n");

		html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:")
			.Append(PageBackground).Append(";\">\n<tr>\n<td align=\"center\" style=\"padding:24px 16px;\">\n");

		AppendCard(html, email);
		AppendFooter(html, email);

		html.Append("</td>\n</tr>\n</table>\n");
		html.Append("</body>\n</html>\n");

		return html.ToString();
	}

	private static void AppendCard(StringBuilder html, ComposedEmail email)
	{
		html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;background-color:")
			.Append(CardBackground).Append(";border-radius:8px;\">\n");

		html.Append("<tr>\n<td style=\"padding:24px 32px 0;font-family:").Append(FontStack)
			.Append(";font-size:22px;font-weight:700;color:").Append(BrandDark).Append(";\">Afunto</td>\n</tr>\n");

		html.Append("<tr>\n<td style=\"padding:16px 32px 32px;font-family:").Append(FontStack)
			.Append(";font-size:16px;line-height:1.5;color:").Append(TextColor).Append(";\">\n");

		AppendParagraph(html, email.Greeting);
		AppendParagraph(html, email.Intro);
		AppendFacts(html, email.Facts);
		AppendLines(html, email.Lines);
		if (email.Outro is not null)
			AppendParagraph(html, email.Outro);
		AppendButton(html, email.Action);
		html.Append("<p style=\"margin:0;\">").Append(EncodeMultiline(email.Closing)).Append("</p>\n");

		html.Append("</td>\n</tr>\n</table>\n");
	}

	private static void AppendParagraph(StringBuilder html, string text) =>
		html.Append("<p style=\"").Append(ParagraphStyle).Append("\">").Append(EncodeMultiline(text)).Append("</p>\n");

	private static void AppendFacts(StringBuilder html, IReadOnlyList<ComposedFact> facts)
	{
		if (facts.Count == 0)
			return;

		html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 16px;border-collapse:collapse;\">\n");
		foreach (var fact in facts)
		{
			html.Append("<tr>\n");
			html.Append("<td style=\"padding:4px 16px 4px 0;vertical-align:top;white-space:nowrap;font-weight:600;color:")
				.Append(MutedColor).Append(";\">").Append(Encode(fact.Label)).Append("</td>\n");
			html.Append("<td style=\"padding:4px 0;vertical-align:top;\">")
				.Append(string.Join("<br>", fact.Values.Select(Encode))).Append("</td>\n");
			html.Append("</tr>\n");
		}
		html.Append("</table>\n");
	}

	private static void AppendLines(StringBuilder html, IReadOnlyList<string> lines)
	{
		if (lines.Count == 0)
			return;

		html.Append("<ul style=\"margin:0 0 16px;padding-left:20px;\">\n");
		foreach (var line in lines)
			html.Append("<li style=\"margin:0 0 4px;\">").Append(Encode(line)).Append("</li>\n");
		html.Append("</ul>\n");
	}

	private static void AppendButton(StringBuilder html, EmailLink action)
	{
		html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 24px;\">\n<tr>\n");
		html.Append("<td style=\"border-radius:6px;background-color:").Append(Brand).Append(";\">");
		html.Append("<a href=\"").Append(Encode(action.Url))
			.Append("\" style=\"display:inline-block;padding:12px 20px;font-family:").Append(FontStack)
			.Append(";font-size:16px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:6px;\">")
			.Append(Encode(action.Label)).Append("</a>");
		html.Append("</td>\n</tr>\n</table>\n");
	}

	private static void AppendFooter(StringBuilder html, ComposedEmail email)
	{
		html.Append("<p style=\"max-width:560px;margin:16px auto 0;font-family:").Append(FontStack)
			.Append(";font-size:13px;line-height:1.5;color:").Append(MutedColor).Append(";\">\n");
		html.Append(Encode(email.FooterNote)).Append("<br>\n");
		AppendFooterLink(html, email.Settings);
		if (email.Unsubscribe is not null)
		{
			html.Append(" &middot; ");
			AppendFooterLink(html, email.Unsubscribe);
		}
		html.Append("\n</p>\n");
	}

	private static void AppendFooterLink(StringBuilder html, EmailLink link) =>
		html.Append("<a href=\"").Append(Encode(link.Url)).Append("\" style=\"color:").Append(MutedColor)
			.Append(";text-decoration:underline;\">").Append(Encode(link.Label)).Append("</a>");

	private static string Encode(string text) => Encoder.Encode(text);

	// Line by line: the encoder would otherwise turn "\n" itself into an entity.
	private static string EncodeMultiline(string text) =>
		string.Join("<br>\n", text.Split('\n').Select(Encode));
}
