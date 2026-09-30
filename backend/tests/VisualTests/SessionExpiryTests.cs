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
	[Test]
	public async Task AuthenticatedRequest_Returns401_RedirectsToKeycloakSignIn()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");
		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);

		await MockAllV1GetRequestsAsUnauthorizedAsync();

		await Page.ReloadAsync();

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
