using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class EmailDeliveryTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	private const string Realm = "afunto";

	[Test]
	public async Task CreateEngagement_DeliversConfirmationToMailpit_ThroughBackendSmtpConfig()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var mailpit = Fixture.GetEndpoint("mailpit", "webui");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var suffix = Guid.NewGuid().ToString("N");
		var title = $"EmailDelivery {suffix}";

		var opportunityId = await CreateIndividualContactOpportunityAsync(title);
		var detailUrl = $"{origin}/volunteer-opportunities/{opportunityId}";

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");
		await Page.GotoAsync(detailUrl);
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.GetByRole(AriaRole.Button, new() { Name = "Express interest" }).ClickAsync();
		await Page.WaitForSelectorAsync("[role='dialog']");
		await Page.Locator("#sign-up-message").FillAsync($"Delivery check for {suffix}");

		await Page.Locator("[role='dialog']").GetByRole(AriaRole.Button, new() { Name = "Express interest" }).ClickAsync();
		await Page.WaitForSelectorAsync("[role='dialog']", new() { State = WaitForSelectorState.Detached });

		await AssertMailpitReceivedMessageToAsync(mailpit, "vera@example.com", subjectContains: suffix);
	}

	private static async Task AssertMailpitReceivedMessageToAsync(
		Uri mailpit, string recipientEmail, string? subjectContains = null)
	{
		using var http = new HttpClient { BaseAddress = mailpit };
		var deadline = DateTime.UtcNow.AddSeconds(30);

		while (DateTime.UtcNow < deadline)
		{
			var response = await http.GetAsync("/api/v1/messages?limit=50");
			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<JsonElement>();
				if (body.TryGetProperty("messages", out var messages))
				{
					var found = messages.EnumerateArray().Any(message =>
						message.TryGetProperty("To", out var to)
						&& to.EnumerateArray().Any(recipient =>
							recipient.TryGetProperty("Address", out var address)
							&& string.Equals(address.GetString(), recipientEmail, StringComparison.OrdinalIgnoreCase))
						&& (subjectContains is null
							|| (message.TryGetProperty("Subject", out var subject)
								&& (subject.GetString()?.Contains(subjectContains, StringComparison.Ordinal) ?? false))));

					if (found)
						return;
				}
			}

			await Task.Delay(1000);
		}

		throw new TimeoutException(
			$"Mailpit did not receive a message to '{recipientEmail}'"
			+ (subjectContains is null ? "" : $" with subject containing '{subjectContains}'")
			+ " within 30s.");
	}

	private async Task<string> CreateIndividualContactOpportunityAsync(string title)
	{
		var keycloak = Fixture.GetEndpoint("keycloak");
		using var tokenHttp = new HttpClient { BaseAddress = keycloak };
		var tokenResponse = await tokenHttp.PostAsync(
			$"/realms/{Realm}/protocol/openid-connect/token",
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

		var backend = Fixture.GetEndpoint("backend");
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

		var orgsResponse = await http.GetAsync("/v1/organizations");
		orgsResponse.EnsureSuccessStatusCode();
		var orgs = await orgsResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = orgs.EnumerateArray().First().GetProperty("id").GetString();

		var draftResponse = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
		{
			titleDe = title,
			descriptionDe = "Created by EmailDeliveryTests",
			organizationId,
			isRemote = true,
			occurrence = "OneTime",
			participationType = "IndividualContact",
			checkInMethod = "None",
			validUntil = DateTimeOffset.UtcNow.AddDays(30),
			isDraft = true,
		});
		draftResponse.EnsureSuccessStatusCode();
		var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
		var opportunityId = draft.GetProperty("id").GetString()!;

		(await http.PostAsync($"/v1/volunteer-opportunities/{opportunityId}/publish", content: null))
			.EnsureSuccessStatusCode();

		return opportunityId;
	}
}
