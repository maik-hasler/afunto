using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

// Two of this class's three cases moved (#2162): the hard-navigation
// already-applied case became an absence assertion paired with the existing
// positive "Confirmed"/"Pending" render in
// F/pages/VolunteerOpportunityDetailPage.test.tsx, and the duplicate-signup
// case's frontend half (the 409 -> "already signed up" mapping, not
// "Unknown error") moved to F/components/SignUpModal.test.tsx - its backend
// half was already covered by IntegrationTests'
// CreateEngagement_ShouldReturn409_WhenVolunteerAlreadySignedUp. This one
// case stays: it drives two independent browser tabs racing an anonymous and
// an authenticated GET against the real backend, which is exactly the kind
// of transient auth-hydration timing the RTL test harness resolves
// synchronously and so cannot reproduce.
[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class OpportunityApplicationStateTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task DetailPage_KeepsAlreadyAppliedStatus_WhenEarlierUnauthenticatedResponseResolvesLast()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var opportunityId = await CreateIndividualContactOpportunityAsync("RaceGuard");
		var detailUrl = $"{origin}/volunteer-opportunities/{opportunityId}";

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");
		await Expect(Page.Locator("main")).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Page.GotoAsync(detailUrl);
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
		await Page.GetByRole(AriaRole.Button, new() { Name = "Express interest" }).ClickAsync();
		await Page.Locator("textarea").FillAsync("Applying via race-guard regression check.");
		await Page.Locator("[role='dialog']").GetByRole(AriaRole.Button, new() { Name = "Express interest" }).ClickAsync();
		await Expect(Page.Locator("[role='dialog']")).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

		var releaseAnonymousRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Page.RouteAsync($"**/v1/volunteer-opportunities/{opportunityId}", async route =>
		{
			if (route.Request.Method == "GET" && !route.Request.Headers.ContainsKey("authorization"))
			{
				await releaseAnonymousRead.Task;
			}

			await route.ContinueAsync();
		});

		await Page.GotoAsync(detailUrl);

		var signUpStatus = Page.GetByTestId("application-status")
			.GetByText("Your expression of interest", new() { Exact = true });

		await Expect(signUpStatus).ToBeVisibleAsync(new() { Timeout = 15_000 });

		releaseAnonymousRead.TrySetResult();
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Expect(signUpStatus).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Express interest" }))
			.Not.ToBeVisibleAsync();
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
			titleDe = $"OpportunityApplicationState {label} {suffix}",
			descriptionDe = "Created by OpportunityApplicationStateTests",
			organizationId,
			isRemote = true,
			occurrence = "OneTime",
			participationType = "IndividualContact",
			checkInMethod = "None",
			validUntil = DateTimeOffset.UtcNow.AddDays(30),
			isDraft = false,
		});
		oppResponse.EnsureSuccessStatusCode();
		var opportunity = await oppResponse.Content.ReadFromJsonAsync<JsonElement>();
		return opportunity.GetProperty("id").GetString()!;
	}
}
