using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class HomePageOrgCtaTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task Anonymous_HeroOrgCta_RedirectsToKeycloakRegistrationEndpoint()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		await Page.GotoAsync(frontend.ToString());
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.GetByRole(AriaRole.Button, new() { Name = "Create an organization" })
			.First.ClickAsync();

		await Expect(Page).ToHaveURLAsync(
			new Regex(@"/realms/afunto/protocol/openid-connect/registrations"));
		await Expect(Page.Locator("#kc-register-form")).ToBeVisibleAsync(new() { Timeout = 30_000 });
	}
}
