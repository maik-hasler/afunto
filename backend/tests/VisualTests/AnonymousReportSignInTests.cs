using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class AnonymousReportSignInTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task VolunteerOpportunityDetailPage_AnonymousReportClick_RedirectsToKeycloakSignIn()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await Page.GotoAsync($"{origin}/opportunities");
		await Expect(Page.Locator("h1")).ToBeVisibleAsync();

		var firstCard = Page.Locator("a[href*='/volunteer-opportunities/']").First;
		try
		{
			await firstCard.WaitForAsync(new() { Timeout = 15_000 });
		}
		catch (TimeoutException)
		{
			Skip.Test("no opportunities seeded");
		}

		var href = await firstCard.GetAttributeAsync("href");
		Skip.When(href is null, "opportunity card had no href attribute");

		await Page.GotoAsync($"{origin}{href}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		var reportButton = Page.GetByTestId("report-opportunity");
		await Expect(reportButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);
		await reportButton.ClickAsync();

		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Expect(Page).ToHaveURLAsync(new Regex(@"/realms/afunto/protocol/openid-connect/auth"));
	}

	[Test]
	public async Task OrganizationProfilePage_AnonymousReportClick_RedirectsToKeycloakSignIn()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await Page.GotoAsync($"{origin}/opportunities");
		await Expect(Page.Locator("h1").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

		var orgLink = Page.GetByTestId("opportunity-org-link").First;
		await Expect(orgLink).ToBeVisibleAsync(new() { Timeout = 15_000 });
		var href = await orgLink.GetAttributeAsync("href");

		await Page.GotoAsync($"{origin}{href}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		var reportButton = Page.GetByTestId("report-organization");
		await Expect(reportButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);
		await reportButton.ClickAsync();

		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Expect(Page).ToHaveURLAsync(new Regex(@"/realms/afunto/protocol/openid-connect/auth"));
	}
}
