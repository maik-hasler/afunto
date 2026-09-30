using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class SignOutLanguagePersistenceTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task SignOut_PreservesExplicitLanguageChoice()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await AuthHelper.LoginAsync(Page, frontend, "vera", "vera123");

		await Page.GetByTestId("language-selector-trigger").ClickAsync();
		await Page.GetByTestId("language-selector-menu")
			.GetByRole(AriaRole.Button, new() { Name = "Deutsch" })
			.ClickAsync();
		await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");

		await Page.GetByRole(AriaRole.Button, new() { Name = "Benutzermenü" }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Abmelden" }).ClickAsync();

		await Page.WaitForURLAsync($"{origin}/", new() { Timeout = 30_000 });
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).First)
			.ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");

		var explicitChoice = await Page.EvaluateAsync<string?>(
			"() => localStorage.getItem('afunto:language-explicit')");
		var storedLanguage = await Page.EvaluateAsync<string?>(
			"() => localStorage.getItem('i18nextLng')");

		explicitChoice.Should().Be("true", "sign-out must not clear the explicit language choice flag");
		storedLanguage.Should().Be("de", "sign-out must not revert the previously chosen UI language");
	}
}
