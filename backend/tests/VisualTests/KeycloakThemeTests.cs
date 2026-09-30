using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class KeycloakThemeTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	private const string Realm = "afunto";
	private const string FrontendClientId = "frontend";

	private const string SiteUrl = "https://afunto.maik-hasler.de";

	private const string ThrowawayPassword = "Throwaway123";

	private string AuthUrl(string endpoint = "auth", string? locale = null)
	{
		var keycloak = Fixture.GetEndpoint("keycloak").ToString().TrimEnd('/');
		var frontend = Fixture.GetEndpoint("frontend").ToString().TrimEnd('/');

		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
		var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

		var query = new Dictionary<string, string>
		{
			["client_id"] = FrontendClientId,
			["redirect_uri"] = frontend,
			["response_type"] = "code",
			["scope"] = "openid",
			["state"] = "theme-test",
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
		};
		if (locale is not null)
			query["ui_locales"] = locale;

		var q = string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
		return $"{keycloak}/realms/{Realm}/protocol/openid-connect/{endpoint}?{q}";
	}

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

	private async Task AssertThemeShellAsync(string label)
	{
		await Expect(Page.Locator(".auth-card")).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(Page.Locator(".auth-logo")).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();

		// The card lays out - and so counts as visible - before the render-blocking
		// stylesheet has been applied: Playwright's visibility check forces a layout,
		// which Chromium performs against the still-unstyled DOM. A one-shot read at
		// that moment returns rgba(0, 0, 0, 0) whenever Keycloak serves the CSS a beat
		// behind the HTML - which is what it does under load right after the sign-in
		// redirect, where this failed. Auto-retrying instead, so a styled card also
		// gates the measurement below rather than it measuring an unstyled one.
		await Expect(Page.Locator(".auth-card"))
			.ToHaveCSSAsync("background-color", "rgb(255, 255, 255)", new() { Timeout = 15_000 });

		await AssertVerticalGapBetweenAsync(
			Page.Locator(".auth-card"), Page.Locator(".auth-back"), label);
	}

	[Test]
	public async Task Login_RendersThemeShellWithBackLinkBelowCard()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		await AssertThemeShellAsync("login");
		await Expect(Page.Locator(".card-eyebrow")).ToHaveTextAsync("Account");
		await Expect(Page.Locator("#password")).ToBeVisibleAsync();
		await Expect(Page.Locator("#kc-login")).ToBeVisibleAsync();

		await Expect(Page.Locator("#kc-login")).ToHaveAttributeAsync("value", "Sign in");
	}

	[Test]
	public async Task Login_LanguageSwitcher_HasAccessibleName()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		var trigger = Page.Locator(".lang-trigger");
		await Expect(trigger).ToHaveAttributeAsync(
			"aria-label", "EN - Switch language, currently English");

		await trigger.ClickAsync();
		var menu = Page.Locator(".lang-menu");
		await Expect(menu).ToBeVisibleAsync();
		await Expect(menu).ToHaveAttributeAsync("aria-label", "Switch language");
		await Expect(menu.Locator(".lang-item")).ToHaveCountAsync(2);
	}

	[Test]
	public async Task Login_PrimaryButton_UsesTheProductsOwnGreen()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#kc-login")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		// brand-700. Auto-retrying, for the same reason as the theme shell above: the
		// stylesheet can still be in flight when the button first lays out.
		await Expect(Page.Locator("#kc-login"))
			.ToHaveCSSAsync("background-color", "rgb(34, 105, 71)");
	}

	[Test]
	public async Task Login_ForgotPasswordLink_MatchesRegisterLinkTreatment()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		var forgotPassword = Page.GetByRole(AriaRole.Link, new() { Name = "Forgot password?", Exact = true });
		await Expect(forgotPassword).ToBeVisibleAsync(new() { Timeout = 30_000 });

		// The same brand-700 treatment as "Register".
		await Expect(forgotPassword).ToHaveCSSAsync("color", "rgb(34, 105, 71)");
	}

	[Test]
	public async Task Login_FailedAttempt_AssociatesErrorWithBothFieldsAndRecolorsFocusRing()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		await Page.Locator("#username").FillAsync($"no-such-user-{Guid.NewGuid():N}");
		await Page.Locator("#password").FillAsync("wrong-password");
		await Page.Locator("#kc-login").ClickAsync();

		await Expect(Page.Locator("#input-error")).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(Page.Locator("#username")).ToHaveAttributeAsync("aria-invalid", "true");
		await Expect(Page.Locator("#username")).ToHaveAttributeAsync("aria-describedby", "input-error");
		await Expect(Page.Locator("#password")).ToHaveAttributeAsync("aria-invalid", "true");
		await Expect(Page.Locator("#password")).ToHaveAttributeAsync("aria-describedby", "input-error");

		await Page.Locator("#username").FocusAsync();
		// The focus ring on an invalid, focused field turns red rather than staying the
		// brand green.
		await Expect(Page.Locator("#username")).ToHaveCSSAsync("outline-color", "rgb(220, 38, 38)");
	}

	[Test]
	public async Task Login_LanguageMenu_LabelsAreEndonymsRegardlessOfCurrentLocale()
	{
		foreach (var locale in new[] { "en", "de" })
		{
			await Page.GotoAsync(AuthUrl(locale: locale));
			await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

			await Page.Locator(".lang-trigger").ClickAsync();
			var items = Page.Locator(".lang-menu .lang-item");
			await Expect(items).ToHaveCountAsync(2);

			var labels = (await items.AllTextContentsAsync()).Select(l => l.Trim());
			labels.Should().BeEquivalentTo(["Deutsch", "English"],
				$"locale={locale}: the language menu should list endonyms for both languages");
		}
	}

	[Test]
	public async Task Login_AuthFadeUpKeyframe_NeverDipsBelowFullOpacity()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator(".auth-card")).ToBeVisibleAsync(new() { Timeout = 15_000 });

		var fromOpacity = await Page.EvaluateAsync<string?>(@"
			() => {
				for (const sheet of document.styleSheets) {
					let rules;
					try { rules = sheet.cssRules; } catch { continue; }
					for (const rule of rules) {
						const keyframesRules = rule instanceof CSSKeyframesRule
							? [rule]
							: rule instanceof CSSMediaRule
								? [...rule.cssRules].filter(r => r instanceof CSSKeyframesRule)
								: [];
						for (const kf of keyframesRules) {
							if (kf.name !== 'auth-fade-up') continue;
							for (const frame of kf.cssRules) {
								if (frame.keyText === 'from' || frame.keyText === '0%') {
									return frame.style.opacity || null;
								}
							}
						}
					}
				}
				return null;
			}");

		fromOpacity.Should().Be("1",
			"the auth-fade-up keyframe's starting frame must stay at full opacity - "
			+ "the sign-in card must never render at reduced contrast, not even "
			+ "transiently during its entrance animation");
	}

	[Test]
	public async Task Register_OmitsPasswordFields_AndSaysWhy()
	{
		await Page.GotoAsync(AuthUrl("registrations", locale: "en"));
		await Expect(Page.Locator("#email")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		await AssertThemeShellAsync("register");
		await Expect(Page.Locator("#username")).ToBeVisibleAsync();
		await Expect(Page.Locator("#password")).ToHaveCountAsync(0);
		await Expect(Page.Locator(".card-lead"))
			.ToContainTextAsync("set your password right after");
	}

	[Test]
	public async Task Register_EmailAndUsername_AreMarkedRequiredForAssistiveTech()
	{
		await Page.GotoAsync(AuthUrl("registrations", locale: "en"));
		await Expect(Page.Locator("#email")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		foreach (var id in new[] { "#email", "#username" })
		{
			await Expect(Page.Locator(id)).ToHaveAttributeAsync("required", "");
			await Expect(Page.Locator(id)).ToHaveAttributeAsync("aria-required", "true");
		}
	}

	[Test]
	public async Task ResetPassword_IsThemed()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		await Page.GetByRole(AriaRole.Link, new() { Name = "Forgot password?" }).ClickAsync();
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await AssertThemeShellAsync("reset-password");
		await Expect(Page.Locator(".card-eyebrow")).ToHaveTextAsync("Account recovery");
	}

	[Test]
	public async Task VerifyEmail_IsThemed()
	{
		var username = $"theme-verify-{Guid.NewGuid():N}"[..24];
		var userId = await Fixture.CreateThrowawayUserAsync(
			username, ThrowawayPassword, emailVerified: false, requiredActions: []);
		try
		{
			await SignInThroughKeycloakUiAsync(username);

			await AssertThemeShellAsync("verify-email");
			await Expect(Page.Locator(".card-eyebrow")).ToHaveTextAsync("Confirm your email");

			await Expect(Page.Locator(".instruction")).ToBeVisibleAsync();
			var color = await Page.Locator(".instruction").First
				.EvaluateAsync<string>("el => getComputedStyle(el).color");
			color.Should().NotBe("rgb(0, 0, 0)",
				"verify-email: .instruction should pick up the theme's prose styling, not browser defaults");
		}
		finally
		{
			await Fixture.DeleteUserAsync(userId);
		}
	}

	[Test]
	public async Task UpdatePassword_FloatingLabelsAttachToTheirField()
	{
		var username = $"theme-pw-{Guid.NewGuid():N}"[..24];
		var userId = await Fixture.CreateThrowawayUserAsync(
			username, ThrowawayPassword, emailVerified: true, requiredActions: ["UPDATE_PASSWORD"]);
		try
		{
			await SignInThroughKeycloakUiAsync(username);

			await Expect(Page.Locator("#password-new")).ToBeVisibleAsync(new() { Timeout = 15_000 });
			await AssertThemeShellAsync("update-password");
			await Expect(Page.Locator(".card-eyebrow")).ToHaveTextAsync("Password");
			await Expect(Page.Locator("#password-confirm")).ToBeVisibleAsync();

			var labelSitsOnField = await Page.EvaluateAsync<bool>(
				"""
				() => {
					const input = document.getElementById('password-new');
					const label = document.querySelector('label[for="password-new"]');
					if (!input || !label) return false;
					const i = input.getBoundingClientRect();
					const l = label.getBoundingClientRect();
					return l.left >= i.left && l.right <= i.right
						&& l.top >= i.top && l.bottom <= i.bottom;
				}
				""");
			labelSitsOnField.Should().BeTrue(
				"update-password: the floating label should sit inside its own field, "
				+ "which only holds when the template emits label as a sibling of the input");

			await Page.Locator("button[aria-controls='password-new']").ClickAsync();
			await Expect(Page.Locator("#password-new")).ToHaveAttributeAsync("type", "text");
		}
		finally
		{
			await Fixture.DeleteUserAsync(userId);
		}
	}

	[Test]
	public async Task UpdateProfile_IsThemed()
	{
		var username = $"theme-profile-{Guid.NewGuid():N}"[..24];
		var userId = await Fixture.CreateThrowawayUserAsync(
			username, ThrowawayPassword, emailVerified: true, requiredActions: ["UPDATE_PROFILE"]);
		try
		{
			await SignInThroughKeycloakUiAsync(username);

			await Expect(Page.Locator("#kc-update-profile-form")).ToBeVisibleAsync(new() { Timeout = 15_000 });
			await AssertThemeShellAsync("update-profile");

			var labelIsStatic = await Page.EvaluateAsync<bool>(
				"""
				() => {
					const label = document.querySelector('.form-label-wrapper .form-label');
					return !!label && getComputedStyle(label).position === 'static';
				}
				""");
			labelIsStatic.Should().BeTrue(
				"update-profile: labels rendered above their input should be statically positioned");
		}
		finally
		{
			await Fixture.DeleteUserAsync(userId);
		}
	}

	[Test]
	public async Task ErrorPage_OffersAVisibleWayBack()
	{
		var keycloak = Fixture.GetEndpoint("keycloak").ToString().TrimEnd('/');
		await Page.GotoAsync(
			$"{keycloak}/realms/{Realm}/login-actions/action-token?key=not-a-real-key&client_id={FrontendClientId}");

		await Expect(Page.Locator("#kc-error-message")).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await AssertThemeShellAsync("error");

		var backLink = Page.Locator("#backToApplication");
		await Expect(backLink).ToBeVisibleAsync();

		var contrast = await backLink.EvaluateAsync<string[]>(
			"el => { const s = getComputedStyle(el); return [s.color, s.backgroundColor]; }");
		contrast[0].Should().NotBe(contrast[1],
			"error page: the back-to-application button's label must not be the same color as its fill");

		await Expect(backLink).ToHaveAttributeAsync("href", SiteUrl);
	}

	[Test]
	public async Task LogoutConfirm_OffersACancel()
	{
		var keycloak = Fixture.GetEndpoint("keycloak").ToString().TrimEnd('/');
		await Page.GotoAsync(
			$"{keycloak}/realms/{Realm}/protocol/openid-connect/logout?client_id={FrontendClientId}");

		await Expect(Page.Locator("#kc-logout")).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(Page.Locator(".auth-card")).ToBeVisibleAsync();
		await Expect(Page.Locator(".auth-logo")).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
		await Expect(Page.Locator("#kc-logout-cancel")).ToBeVisibleAsync();

		await Expect(Page.Locator(".auth-back")).ToHaveCountAsync(0);
	}

	[Test]
	public async Task Pages_HaveDistinctBrowserTitles()
	{
		var keycloak = Fixture.GetEndpoint("keycloak").ToString().TrimEnd('/');

		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		var loginTitle = await Page.TitleAsync();

		await Page.GotoAsync(AuthUrl("registrations", locale: "en"));
		await Expect(Page.Locator("#email")).ToBeVisibleAsync(new() { Timeout = 15_000 });
		var registerTitle = await Page.TitleAsync();

		await Page.GotoAsync(
			$"{keycloak}/realms/{Realm}/protocol/openid-connect/logout?client_id={FrontendClientId}&ui_locales=en");
		await Expect(Page.Locator("#kc-logout")).ToBeVisibleAsync(new() { Timeout = 15_000 });
		var logoutTitle = await Page.TitleAsync();

		loginTitle.Should().Contain("Afunto");
		new[] { loginTitle, registerTitle, logoutTitle }.Distinct().Should().HaveCount(3,
			"each page should carry its own pageTitle rather than inheriting login.ftl's");
	}

	[Test]
	public async Task GermanPages_KeepTheProductsDuRegister()
	{
		foreach (var (endpoint, marker) in new[] { ("auth", "#username"), ("registrations", "#email") })
		{
			await Page.GotoAsync(AuthUrl(endpoint, locale: "de"));
			await Expect(Page.Locator(marker)).ToBeVisibleAsync(new() { Timeout = 30_000 });

			var card = await Page.Locator(".auth-card").InnerTextAsync();

			card.Should().NotMatchRegex(@"\bSie\b",
				$"{endpoint}: the German copy should address the user as du, not Sie");
			card.Should().NotMatchRegex(@"\bIhre[nmrs]?\b",
				$"{endpoint}: the German copy should use dein/deine, not Ihre");
		}
	}

	[Test]
	public async Task Login_HasNoSeriousA11yViolations()
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		AssertNoSeriousViolations(await Page.RunAxe(), "login");
	}

	[Test]
	public async Task Register_HasNoSeriousA11yViolations()
	{
		await Page.GotoAsync(AuthUrl("registrations", locale: "en"));
		await Expect(Page.Locator("#email")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		AssertNoSeriousViolations(await Page.RunAxe(), "register");
	}

	[Test]
	public async Task UpdatePassword_HasNoSeriousA11yViolations()
	{
		var username = $"theme-a11y-{Guid.NewGuid():N}"[..24];
		var userId = await Fixture.CreateThrowawayUserAsync(
			username, ThrowawayPassword, emailVerified: true, requiredActions: ["UPDATE_PASSWORD"]);
		try
		{
			await SignInThroughKeycloakUiAsync(username);
			await Expect(Page.Locator("#password-new")).ToBeVisibleAsync(new() { Timeout = 15_000 });

			AssertNoSeriousViolations(await Page.RunAxe(), "update-password");
		}
		finally
		{
			await Fixture.DeleteUserAsync(userId);
		}
	}

	private async Task SignInThroughKeycloakUiAsync(string username)
	{
		await Page.GotoAsync(AuthUrl(locale: "en"));
		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });

		await Page.Locator("#username").FillAsync(username);
		await Page.Locator("#password").FillAsync(ThrowawayPassword);
		await Page.Locator("#kc-login").ClickAsync();

		await Expect(Page.Locator("#kc-form-login")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
	}

	private static void AssertNoSeriousViolations(AxeResult result, string label)
	{
		var violations = result.Violations
			.Where(v => v.Impact is "serious" or "critical")
			.ToList();

		if (violations.Count == 0)
			return;

		var summary = string.Join("\n", violations.Select(v =>
			$"[{v.Impact}] {v.Id}: {v.Description}\n"
			+ string.Join("\n", v.Nodes.Select(n => $"  - {n.Html}"))));

		throw new Exception($"Axe found {violations.Count} a11y violation(s) on {label}:\n{summary}");
	}
}
