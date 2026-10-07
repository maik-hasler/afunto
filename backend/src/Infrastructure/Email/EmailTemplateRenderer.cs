using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Common.Email;
using Application.Common.Localization;
using Application.Common.Time;

namespace Infrastructure.Email;

// Turns an EmailDraft into subject, plain text and HTML. The wording lives in
// Templates/{language}.json: a shared "layout" (greeting, closing, footer, fact labels,
// date formats) plus one entry per EmailTemplateKind. Every email gets the same
// skeleton - greeting, intro, facts, optional list, optional outro, one button,
// closing, footer - so no template can drift into a different shape or tone (#2402).
internal sealed partial class EmailTemplateRenderer
	: IEmailTemplateRenderer
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true,
	};

	private readonly IEmailLinkBuilder _linkBuilder;
	private readonly IReadOnlyDictionary<string, TemplateFile> _templatesByLanguage;

	public EmailTemplateRenderer(IEmailLinkBuilder linkBuilder)
	{
		_linkBuilder = linkBuilder;
		_templatesByLanguage = new Dictionary<string, TemplateFile>
		{
			["en"] = LoadTemplates("en"),
			["de"] = LoadTemplates("de"),
		};
	}

	public RenderedEmail Render(EmailDraft draft)
	{
		var language = SupportedLanguages.Resolve(draft.Language);
		var file = _templatesByLanguage[language];
		var template = file.Templates[draft.Kind];
		var layout = file.Layout;
		var isPlural = draft.Count > 1;

		var email = new ComposedEmail(
			Language: language,
			Subject: SingleLine(Interpolate(isPlural ? template.SubjectPlural ?? template.Subject : template.Subject, draft.Placeholders)),
			Greeting: Interpolate(layout.Greeting, new Dictionary<string, string> { ["RecipientName"] = draft.RecipientName }),
			Intro: Interpolate(isPlural ? template.IntroPlural ?? template.Intro : template.Intro, draft.Placeholders),
			Facts: [.. draft.Facts.Select(fact => ComposeFact(fact, layout))],
			Lines: [.. draft.Lines.Select(line => Interpolate(LineFormat(template, line.Key, draft.Kind), line.Placeholders))],
			Outro: template.Outro is null ? null : Interpolate(template.Outro, draft.Placeholders),
			Action: new EmailLink(template.Action, draft.ActionUrl),
			Closing: layout.Closing,
			FooterNote: layout.Footer,
			Settings: new EmailLink(layout.Settings, _linkBuilder.NotificationSettings()),
			Unsubscribe: draft.UnsubscribeUrl is null ? null : new EmailLink(layout.Unsubscribe, draft.UnsubscribeUrl));

		return new RenderedEmail(
			email.Subject,
			PlainTextEmailWriter.Write(email),
			HtmlEmailWriter.Write(email),
			draft.UnsubscribeUrl);
	}

	private static ComposedFact ComposeFact(EmailFact fact, LayoutDefinition layout) => fact switch
	{
		EmailFact.Schedule schedule => new ComposedFact(
			schedule.Slots.Count > 1 ? layout.SchedulePlural : layout.Schedule,
			[.. schedule.Slots.OrderBy(slot => slot.Start).Select(slot => FormatTimeRange(slot, layout))]),
		EmailFact.Location location => new ComposedFact(layout.Location, [location.Address ?? layout.Online]),
		EmailFact.Reason reason => new ComposedFact(layout.Reason, [reason.Text]),
		_ => throw new InvalidOperationException($"Unhandled email fact '{fact.GetType().Name}'."),
	};

	// Shown in the platform's canonical zone, the same one every other date in the app
	// and in the reminder job is bucketed by - volunteers and organizers are all local to it.
	private static string FormatTimeRange(EmailTimeRange slot, LayoutDefinition layout)
	{
		var culture = CultureInfo.GetCultureInfo(layout.Culture);
		var start = TimeZoneInfo.ConvertTime(slot.Start, CanonicalTimeZone.Value);
		var end = TimeZoneInfo.ConvertTime(slot.End, CanonicalTimeZone.Value);

		var endText = start.Date == end.Date
			? end.ToString(layout.TimePattern, culture)
			: $"{end.ToString(layout.DatePattern, culture)}, {end.ToString(layout.TimePattern, culture)}";

		return Interpolate(layout.TimeRange, new Dictionary<string, string>
		{
			["Date"] = start.ToString(layout.DatePattern, culture),
			["Start"] = start.ToString(layout.TimePattern, culture),
			["End"] = endText,
		});
	}

	private static string LineFormat(TemplateDefinition template, string key, EmailTemplateKind kind) =>
		template.Lines is not null && template.Lines.TryGetValue(key, out var format)
			? format
			: throw new InvalidOperationException($"Email template '{kind}' has no line format '{key}'.");

	// One pass over the template, so a value that happens to contain "{Something}" (an
	// opportunity title, say) is never itself interpolated. A placeholder the caller did
	// not supply throws instead of leaking "{OpportunityTitle}" into a sent email.
	private static string Interpolate(string template, IReadOnlyDictionary<string, string> placeholders) =>
		PlaceholderPattern().Replace(template, match =>
			placeholders.TryGetValue(match.Groups[1].Value, out var value)
				? value
				: throw new InvalidOperationException($"No value supplied for email placeholder '{match.Value}'."));

	private static string SingleLine(string text) => text.ReplaceLineEndings(" ");

	private static TemplateFile LoadTemplates(string language)
	{
		var resourceName = $"Infrastructure.Email.Templates.{language}.json";
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Embedded email template resource '{resourceName}' was not found.");

		var raw = JsonSerializer.Deserialize<RawTemplateFile>(stream, JsonOptions)
			?? throw new InvalidOperationException($"Embedded email template resource '{resourceName}' is empty or invalid.");

		var templates = raw.Templates.ToDictionary(
			kvp => Enum.Parse<EmailTemplateKind>(kvp.Key),
			kvp => kvp.Value);

		// Fail at startup, not at the first send of a rarely used email.
		var missing = Enum.GetValues<EmailTemplateKind>().Where(kind => !templates.ContainsKey(kind)).ToList();
		if (missing.Count > 0)
			throw new InvalidOperationException(
				$"Embedded email template resource '{resourceName}' has no template for: {string.Join(", ", missing)}.");

		return new TemplateFile(raw.Layout, templates);
	}

	[GeneratedRegex(@"\{(\w+)\}")]
	private static partial Regex PlaceholderPattern();

	private sealed record TemplateFile(
		LayoutDefinition Layout,
		IReadOnlyDictionary<EmailTemplateKind, TemplateDefinition> Templates);

	private sealed record RawTemplateFile(
		[property: JsonPropertyName("layout")] LayoutDefinition Layout,
		[property: JsonPropertyName("templates")] Dictionary<string, TemplateDefinition> Templates);

	private sealed record LayoutDefinition(
		[property: JsonPropertyName("culture")] string Culture,
		[property: JsonPropertyName("datePattern")] string DatePattern,
		[property: JsonPropertyName("timePattern")] string TimePattern,
		[property: JsonPropertyName("timeRange")] string TimeRange,
		[property: JsonPropertyName("greeting")] string Greeting,
		[property: JsonPropertyName("closing")] string Closing,
		[property: JsonPropertyName("schedule")] string Schedule,
		[property: JsonPropertyName("schedulePlural")] string SchedulePlural,
		[property: JsonPropertyName("location")] string Location,
		[property: JsonPropertyName("online")] string Online,
		[property: JsonPropertyName("reason")] string Reason,
		[property: JsonPropertyName("footer")] string Footer,
		[property: JsonPropertyName("settings")] string Settings,
		[property: JsonPropertyName("unsubscribe")] string Unsubscribe);

	private sealed record TemplateDefinition(
		[property: JsonPropertyName("subject")] string Subject,
		[property: JsonPropertyName("intro")] string Intro,
		[property: JsonPropertyName("action")] string Action,
		[property: JsonPropertyName("subjectPlural")] string? SubjectPlural = null,
		[property: JsonPropertyName("introPlural")] string? IntroPlural = null,
		[property: JsonPropertyName("outro")] string? Outro = null,
		[property: JsonPropertyName("lines")] Dictionary<string, string>? Lines = null);
}
