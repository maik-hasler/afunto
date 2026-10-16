using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class SessionExpiryTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	// #2392: an ended session no longer takes the page away on its own - the
	// header offers signing in again, and only that click leaves for Keycloak.
	[Test]
	public async Task AuthenticatedRequest_Returns401_OffersSigningInAgainWithoutLeavingThePage()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");
		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		await MockAllV1GetRequestsAsUnauthorizedAsync();

		await Page.ReloadAsync();

		var signInAgain = Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Erneut anmelden|Sign in again)$") }).First;
		await Expect(signInAgain).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Expect(Page).Not.ToHaveURLAsync(new Regex(@"/realms/afunto/protocol/openid-connect/auth"));

		await signInAgain.ClickAsync();

		await Expect(Page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Expect(Page).ToHaveURLAsync(new Regex(@"/realms/afunto/protocol/openid-connect/auth"));
	}

	private async Task MockAllV1GetRequestsAsUnauthorizedAsync()
	{
		await Page.RouteAsync("**/v1/**", async route =>
		{
			if (route.Request.Method != "GET")
			{
				await route.ContinueAsync();
				return;
			}

			await route.FulfillAsync(new()
			{
				Status = 401,
				ContentType = "application/json",
				Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
				Body = "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.2\",\"status\":401}",
			});
		});
	}
}
