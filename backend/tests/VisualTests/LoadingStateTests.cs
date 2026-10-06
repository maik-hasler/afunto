using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class LoadingStateTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	private static TaskCompletionSource NewResponseGate() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	[Test]
	public async Task DetailPage_ShowsLoadingSkeleton_WhileOpportunityFetches()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var opportunityId = await CreateIndividualContactOpportunityAsync("LoadingSkeleton");
		var detailUrl = $"{origin}/volunteer-opportunities/{opportunityId}";

		var detailResponse = NewResponseGate();
		await Page.RouteAsync($"**/v1/volunteer-opportunities/{opportunityId}", async route =>
		{
			if (route.Request.Method == "GET")
				await detailResponse.Task;
			await route.ContinueAsync();
		});

		await Page.GotoAsync(detailUrl);

		var loadingStatus = Page.Locator("[role='status']:has(.animate-pulse)").First;
		try
		{
			await Expect(loadingStatus).ToBeVisibleAsync();
			await Expect(loadingStatus).ToContainTextAsync("Loading");
			await Expect(loadingStatus.Locator(".animate-pulse")).Not.ToHaveCountAsync(0);
		}
		finally
		{
			detailResponse.TrySetResult();
		}

		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
		await Expect(loadingStatus).Not.ToBeVisibleAsync(new() { Timeout = 5_000 });
		await Expect(Page.Locator("h1")).ToBeVisibleAsync();
	}

	private async Task<string> CreateIndividualContactOpportunityAsync(string label)
	{
		var backend = Fixture.GetEndpoint("backend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var suffix = Guid.NewGuid().ToString("N");

		using var tokenHttp = new HttpClient { BaseAddress = keycloak };
		var tokenResponse = await tokenHttp.PostAsync(
			"/realms/afunto/protocol/openid-connect/token",
			new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "password",
				["client_id"] = "frontend-test",
				["username"] = "olaf",
				["password"] = "olaf123",
				["scope"] = "openid",
			}));
		tokenResponse.EnsureSuccessStatusCode();
		var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
		var token = tokenBody.GetProperty("access_token").GetString();

		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

		var orgsResponse = await http.GetAsync("/v1/organizations");
		orgsResponse.EnsureSuccessStatusCode();
		var orgs = await orgsResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = orgs.EnumerateArray().First().GetProperty("id").GetString();

		var oppResponse = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
		{
			titleDe = $"LoadingState {label} {suffix}",
			descriptionDe = "Created by LoadingStateTests",
			organizationId,
			isRemote = true,
			occurrence = "OneTime",
			participationType = "IndividualContact",
			checkInMethod = "None",
			validUntil = DateTimeOffset.UtcNow.AddDays(30),
			isDraft = false,
		});
		oppResponse.EnsureSuccessStatusCode();
		var opp = await oppResponse.Content.ReadFromJsonAsync<JsonElement>();
		return opp.GetProperty("id").GetString()
			?? throw new InvalidOperationException("Created opportunity had no id.");
	}
}
