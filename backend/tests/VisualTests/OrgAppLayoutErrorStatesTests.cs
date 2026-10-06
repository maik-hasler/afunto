using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class OrgAppLayoutErrorStatesTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task NonOrganizerVisitingOrgApp_Gets403_ShowsNotAuthorizedScreen()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var organizationId = await CreateOrganizationAsync("Org403Screen");

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");

		await Page.GotoAsync($"{origin}/app/{organizationId}/dashboard");

		await Expect(Page.GetByRole(AriaRole.Heading,
			new() { Name = "You don't have access to this organization." }))
			.ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(Page.GetByText("You are not a member of this organization.", new() { Exact = false }))
			.ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Back to Afunto" }))
			.ToBeVisibleAsync();

		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Try again" }))
			.Not.ToBeVisibleAsync();
	}

	private async Task<string> CreateOrganizationAsync(string label)
	{
		var backend = Fixture.GetEndpoint("backend");
		var suffix = Guid.NewGuid().ToString("N");

		var olafSession = await Fixture.SignInAsync("olaf", "olaf123");
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafSession.AccessToken}");

		var response = await PostJsonWithRetryAsync(http, "/v1/organizations", new
		{
			name = $"VisualTests {label} {suffix}",
		});
		response.EnsureSuccessStatusCode();
		var org = await response.Content.ReadFromJsonAsync<JsonElement>();
		return org.GetProperty("id").GetProperty("value").GetString()
			?? throw new InvalidOperationException("Created organization had no id.");
	}
}
