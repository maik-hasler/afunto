using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class AuthGuardTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task MyEngagements_Anonymous_RedirectsToKeycloak()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		try
		{
			await Page.GotoAsync($"{frontend}my-signups", new() { WaitUntil = WaitUntilState.Commit });
		}
		catch (PlaywrightException)
		{
			// GotoAsync may abort if the JS-driven redirect kicks in before commit completes.
			// The redirect itself is what we're testing for - ignore the abort and verify below.
		}

		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Expect(Page.Locator("#password")).ToBeVisibleAsync();
		await Expect(Page).ToHaveURLAsync(new Regex(@"/realms/afunto/protocol/openid-connect/auth"));
	}

	[Test]
	public async Task SignIn_WithValidCredentials_ReachesAuthenticatedHomePage()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.LoginAsync(Page, frontend, "vera", "vera123");

		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "User menu" }))
			.ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }))
			.Not.ToBeVisibleAsync();
	}

	[Test]
	public async Task Header_SignIn_FromNonHomePage_ReturnsToOriginatingPage()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		await Page.GotoAsync($"{frontend}opportunities");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).First.ClickAsync();

		await Page.Locator("#username").WaitForAsync(new() { Timeout = 30_000 });
		await Page.Locator("#username").FillAsync("vera");
		await Page.Locator("#password").FillAsync("vera123");
		await Page.Locator("#kc-login").ClickAsync();

		await Page.GetByRole(AriaRole.Button, new() { Name = "User menu" })
			.WaitForAsync(new() { Timeout = 45_000 });

		await Expect(Page).ToHaveURLAsync(new Regex(@"/opportunities$"));
	}

	[Test]
	public async Task Header_Anonymous_RegisterButton_RedirectsToKeycloakRegistrationEndpoint()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		await Page.GotoAsync(frontend.ToString());
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.GetByRole(AriaRole.Button, new() { Name = "Register" }).First.ClickAsync();

		await Expect(Page).ToHaveURLAsync(
			new Regex(@"/realms/afunto/protocol/openid-connect/registrations"));
		await Expect(Page.Locator("#kc-register-form")).ToBeVisibleAsync(new() { Timeout = 30_000 });
	}
}
