using Application.Common.Email;
using AwesomeAssertions;
using Infrastructure.Common;
using Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace IntegrationTests.Email;

public class EmailTemplateRendererTests
{
	private const string BaseUrl = "https://afunto.example";

	// 08:00 UTC in October is 10:00 in Europe/Berlin (CEST).
	private static readonly EmailTimeRange MorningShift = new(
		new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero),
		new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));

	private static readonly EmailTimeRange NextWeeksShift = new(
		new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero),
		new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

	private readonly EmailTemplateRenderer _sut = new(
		new EmailLinkBuilder(Options.Create(new ApiOptions { FrontendBaseUrl = BaseUrl })));

	[Test]
	[Arguments("de")]
	[Arguments("en")]
	public void Render_ShouldResolveEveryPlaceholder_ForEveryTemplateKind_InEveryLanguage(string language)
	{
		foreach (var kind in Enum.GetValues<EmailTemplateKind>())
		{
			var email = _sut.Render(new EmailDraft(kind, language, "Vera", $"{BaseUrl}/my-signups")
			{
				Placeholders = new Dictionary<string, string>
				{
					["OpportunityTitle"] = "Beach Cleanup",
					["OrganizationName"] = "Beach Cleanup Crew",
					["ExpiryDays"] = "14",
				},
				Facts = [new EmailFact.Schedule([MorningShift]), new EmailFact.Location(null), new EmailFact.Reason("Rain")],
				Lines = kind == EmailTemplateKind.OrganizerDigest
					? [Line("NewSignUp"), Line("Withdrawal")]
					: [],
			});

			email.Subject.Should().NotBeNullOrWhiteSpace();
			email.TextBody.Should().NotContain("{", $"'{kind}' ({language}) left an unresolved placeholder in its text part");
			email.HtmlBody.Should().NotContain("{", $"'{kind}' ({language}) left an unresolved placeholder in its HTML part");
		}
	}

	[Test]
	public void Render_ShouldLayOutTheTextPart_GreetingIntroFactsActionClosingFooter()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "de", "Vera", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Tafel-Ausgabe" },
			Facts = [new EmailFact.Schedule([MorningShift]), new EmailFact.Location("Hauptstraße 1, 12345 Berlin")],
			UnsubscribeUrl = $"{BaseUrl}/unsubscribe?type=EngagementConfirmed",
		});

		email.Subject.Should().Be("Eintragung bestätigt: Tafel-Ausgabe");
		email.TextBody.Should().Be(
			"Hallo Vera,\n\n" +
			"deine Eintragung für „Tafel-Ausgabe“ ist bestätigt. Wir freuen uns auf dich!\n\n" +
			"Termin: Dienstag, 13. Oktober 2026, 10:00 \u2013 12:00 Uhr\n" +
			"Ort: Hauptstraße 1, 12345 Berlin\n\n" +
			$"Meine Eintragungen ansehen: {BaseUrl}/my-signups\n\n" +
			"Viele Grüße\nDein Afunto-Team\n\n" +
			"-- \n" +
			"Du bekommst diese E-Mail, weil du ein Konto bei Afunto hast.\n" +
			$"E-Mail-Einstellungen ändern: {BaseUrl}/profile#email-notifications\n" +
			$"Diese Art von E-Mail abbestellen: {BaseUrl}/unsubscribe?type=EngagementConfirmed\n");
	}

	[Test]
	public void Render_ShouldUsePluralWordingAndListEveryDate_WhenOneEmailCoversSeveralEngagements()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "de", "Vera", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Tafel-Ausgabe" },
			Count = 2,
			Facts = [new EmailFact.Schedule([NextWeeksShift, MorningShift])],
		});

		email.Subject.Should().Be("Eintragungen bestätigt: Tafel-Ausgabe");
		email.TextBody.Should().Contain("deine Eintragungen für „Tafel-Ausgabe“ sind bestätigt.");
		email.TextBody.Should().Contain(
			"Termine:\n" +
			"- Dienstag, 13. Oktober 2026, 10:00 \u2013 12:00 Uhr\n" +
			"- Dienstag, 20. Oktober 2026, 10:00 \u2013 12:00 Uhr\n",
			"dates are listed chronologically whatever order the handler passed them in");
	}

	[Test]
	public void Render_ShouldFormatDatesInEnglish_ForEnglishRecipients()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementReminder, "en", "Vera", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Beach Cleanup" },
			Facts = [new EmailFact.Schedule([MorningShift]), new EmailFact.Location(null)],
		});

		email.Subject.Should().Be("Reminder: Beach Cleanup");
		email.TextBody.Should().StartWith("Hi Vera,\n\n");
		email.TextBody.Should().Contain("Date: Tuesday, 13 October 2026, 10:00 to 12:00\n");
		email.TextBody.Should().Contain("Place: Online\n");
	}

	[Test]
	public void Render_ShouldHtmlEncodeUserInput_InTheHtmlPartOnly()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementCancelled, "en", "<b>Vera</b>", $"{BaseUrl}/opportunities")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "<script>alert(1)</script>" },
			Facts = [new EmailFact.Reason("Too \"few\" & late")],
		});

		email.HtmlBody.Should().NotContain("<script>").And.NotContain("<b>Vera</b>");
		email.HtmlBody.Should().Contain("&lt;script&gt;").And.Contain("&amp;");
		email.TextBody.Should().Contain("<script>alert(1)</script>", "the plain-text part is never interpreted as markup");
	}

	[Test]
	public void Render_ShouldKeepUmlautsReadable_InTheHtmlPart()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "de", "Jürgen", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Grünflächenpflege" },
		});

		email.HtmlBody.Should().Contain("Hallo Jürgen,").And.Contain("„Grünflächenpflege“");
		email.HtmlBody.Should().Contain("Viele Grüße<br>\nDein Afunto-Team", "line breaks in the copy survive HTML encoding");
		email.HtmlBody.Should().Contain("<html lang=\"de\">");
		email.HtmlBody.Should().Contain($"href=\"{BaseUrl}/my-signups\"");
	}

	[Test]
	public void Render_ShouldOmitTheUnsubscribeLink_WhenTheEmailCannotBeUnsubscribedFrom()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementCancelled, "de", "Vera", $"{BaseUrl}/opportunities")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Tafel-Ausgabe" },
		});

		email.UnsubscribeUrl.Should().BeNull();
		email.TextBody.Should().NotContain("abbestellen");
		email.TextBody.Should().Contain($"E-Mail-Einstellungen ändern: {BaseUrl}/profile#email-notifications");
	}

	[Test]
	public void Render_ShouldListOneBulletPerDigestLine()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.OrganizerDigest, "de", "Olaf", $"{BaseUrl}/app/org/dashboard/engagements")
		{
			Lines = [Line("NewSignUp"), Line("Withdrawal")],
		});

		email.TextBody.Should().Contain(
			"- Vera hat sich für „Beach Cleanup“ eingetragen.\n" +
			"- Vera hat die Eintragung für „Beach Cleanup“ zurückgezogen.\n");
		email.HtmlBody.Should().Contain("<li style=\"margin:0 0 4px;\">Vera hat sich für „Beach Cleanup“ eingetragen.</li>");
	}

	[Test]
	public void Render_ShouldNotInterpolatePlaceholderSyntax_InsideAValue()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "en", "Vera", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "{RecipientName} Cleanup" },
		});

		email.Subject.Should().Be("Sign-up confirmed: {RecipientName} Cleanup");
	}

	[Test]
	public void Render_ShouldThrow_WhenAPlaceholderHasNoValue()
	{
		var act = () => _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "de", "Vera", $"{BaseUrl}/my-signups"));

		act.Should().Throw<InvalidOperationException>().WithMessage("*{OpportunityTitle}*");
	}

	[Test]
	public void Render_ShouldFallBackToGerman_WhenLanguageIsUnsupported()
	{
		var email = _sut.Render(new EmailDraft(EmailTemplateKind.EngagementConfirmed, "fr", "Vera", $"{BaseUrl}/my-signups")
		{
			Placeholders = new Dictionary<string, string> { ["OpportunityTitle"] = "Beach Cleanup" },
		});

		email.Subject.Should().Be("Eintragung bestätigt: Beach Cleanup");
	}

	private static EmailLine Line(string key) =>
		new(key, new Dictionary<string, string> { ["VolunteerName"] = "Vera", ["OpportunityTitle"] = "Beach Cleanup" });
}
