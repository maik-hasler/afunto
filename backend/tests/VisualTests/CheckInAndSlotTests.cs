using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class CheckInAndSlotTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task EditOpportunity_SwitchToPINCode_ShowsSetPinOnManagePage()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var suffix = Guid.NewGuid().ToString("N");
		const string pin = "482170";

		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {await AuthHelper.GetTokenAsync(keycloak, "olaf", "olaf123")}");

		var orgResponse = await PostJsonWithRetryAsync(http, "/v1/organizations", new { name = $"CheckInPinSwitch Org {suffix}" });
		orgResponse.EnsureSuccessStatusCode();
		var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = org.GetProperty("id").GetProperty("value").GetString();

		var oppTitle = $"CheckInPinSwitch Opportunity {suffix}";
		var oppResponse = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
		{
			titleDe = oppTitle,
			descriptionDe = "Created by CheckInAndSlotTests",
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
		var opportunityId = opportunity.GetProperty("id").GetString();

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "olaf", "olaf123");
		await Page.GotoAsync($"{origin}/app/{organizationId}/dashboard/opportunities");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		var oppRow = Page.Locator("li", new() { HasText = oppTitle });
		await Expect(oppRow).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await OpportunityRowHelper.ClickActionAsync(oppRow, "opportunity-edit");
		await Page.WaitForSelectorAsync("[role='dialog']");

		await Page.GetByTestId("wizard-stepper-3").ClickAsync();
		await Page.Locator("label:has(input[name='checkInMethod'][value='PINCode'])").ClickAsync();
		var pinInput = Page.Locator("#create-check-in-pin");
		await Expect(pinInput).ToBeVisibleAsync();
		await pinInput.FillAsync(pin);

		await Page.GetByTestId("modal-submit").ClickAsync();
		await Expect(Page.Locator("[role='dialog']")).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

		var manageLink = oppRow.GetByRole(AriaRole.Link, new() { Name = "Manage sign-ups" });
		await Expect(manageLink).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await manageLink.ClickAsync();
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		var pinDisplay = Page.Locator("p.font-mono");
		await Expect(pinDisplay).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(pinDisplay).ToHaveTextAsync(pin);
	}
}
