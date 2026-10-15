using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class VolunteerOpportunityTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task HomePage_RendersOpportunitiesList()
	{
		var frontend = Fixture.GetEndpoint("frontend");

		await Page.GotoAsync(frontend.ToString());
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Expect(Page.Locator("main")).ToBeVisibleAsync();
	}

	[Test]
	public async Task FrequencyFilter_PanelStaysBelowHeader()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await Page.GotoAsync($"{origin}/opportunities");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.GetByTestId("filter-frequency").ClickAsync();
		var oneTimeOption = Page.GetByRole(AriaRole.Button, new() { Name = "One-time" });
		await Expect(oneTimeOption).ToBeVisibleAsync();

		var panel = oneTimeOption.Locator("xpath=..");
		await Expect(panel).ToHaveCSSAsync("z-index", "30");
	}

	[Test]
	public async Task DetailPage_ShowsHomeLink_AndNoShareButton()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await Page.GotoAsync($"{origin}/opportunities");
		await Expect(Page.Locator("h1")).ToBeVisibleAsync();

		var firstCard = Page.Locator("a[href*='/volunteer-opportunities/']").First;
		await Expect(firstCard).ToBeVisibleAsync(new() { Timeout = 15_000 });

		var href = await firstCard.GetAttributeAsync("href");
		Skip.When(href is null, "opportunity card link had no href");

		await Page.GotoAsync($"{origin}{href}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Expect(Page.Locator("nav[aria-label='Breadcrumb']")).ToHaveCountAsync(0);
		await Expect(Page.Locator("main").GetByRole(AriaRole.Link, new() { Name = "Home" }))
			.ToHaveCountAsync(0);
		await Expect(Page.GetByTestId("nav-home")).ToBeVisibleAsync();

		await Expect(Page.GetByTestId("share-opportunity")).ToHaveCountAsync(0);

		// No toolbar row for a visitor any more: #2330 left it to a draft's owner
		// and moved Report to the page footer, where every comparable listing site
		// puts a moderation action.
		await Expect(Page.GetByTestId("opportunity-detail-actions")).ToHaveCountAsync(0);
		await Expect(Page.GetByTestId("report-opportunity")).ToBeVisibleAsync();
	}

	[Test]
	public async Task DetailPage_ContentIsCenteredWithinMain()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await Page.GotoAsync($"{origin}/opportunities");
		await Expect(Page.Locator("h1")).ToBeVisibleAsync();

		var firstCard = Page.Locator("a[href*='/volunteer-opportunities/']").First;
		await Expect(firstCard).ToBeVisibleAsync(new() { Timeout = 15_000 });

		var href = await firstCard.GetAttributeAsync("href");
		Skip.When(href is null, "opportunity card link had no href");

		await Page.GotoAsync($"{origin}{href}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await AssertMaxWidthContentCenteredAsync("Opportunity detail page");
	}

	[Test]
	public async Task DetailPage_ShowsAboutOrganizationCard_SocialProofStat_AndMoreFromOrgTeaser()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var suffix = Guid.NewGuid().ToString("N")[..8];

		var olafToken = (await Fixture.SignInAsync("olaf", "olaf123")).AccessToken;
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafToken}");

		var orgName = $"Detail Enrichment Org {suffix}";
		var orgResponse = await PostJsonWithRetryAsync(http, "/v1/organizations", new { name = orgName });
		orgResponse.EnsureSuccessStatusCode();
		var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = org.GetProperty("id").GetProperty("value").GetString();

		var contactEmail = $"contact-{suffix}@example.test";
		var updateResponse = await http.PutAsJsonAsync($"/v1/organizations/{organizationId}", new
		{
			name = orgName,
			description = "We coordinate volunteers across the region for issue 759 coverage.",
			contactEmail,
			contactPhone = "+49 555 0100",
			website = "https://example.test",
			address = new { street = "Teststrasse", houseNumber = "1", zipCode = "12345", city = "Musterstadt" },
		});
		updateResponse.EnsureSuccessStatusCode();

		async Task<(string Id, string Title)> CreateOpportunityAsync(string label)
		{
			var title = $"{label} {suffix}";
			var response = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
			{
				titleDe = title,
				descriptionDe = $"{label} opportunity for detail enrichment coverage.",
				organizationId,
				isRemote = true,
				occurrence = "OneTime",
				participationType = "IndividualContact",
				checkInMethod = "None",
				validUntil = DateTimeOffset.UtcNow.AddDays(30),
				isDraft = false,
			});
			response.EnsureSuccessStatusCode();
			var body = await response.Content.ReadFromJsonAsync<JsonElement>();
			return (body.GetProperty("id").GetString()!, title);
		}

		var (primaryId, primaryTitle) = await CreateOpportunityAsync("Primary");
		var others = new List<(string Id, string Title)>();
		for (var i = 1; i <= 4; i++)
			others.Add(await CreateOpportunityAsync($"Other{i}"));

		var veraToken = (await Fixture.SignInAsync("vera", "vera123")).AccessToken;
		using var veraHttp = new HttpClient { BaseAddress = backend };
		veraHttp.DefaultRequestHeaders.Add("Authorization", $"Bearer {veraToken}");
		var engagementResponse = await veraHttp.PostAsJsonAsync(
			$"/v1/volunteer-opportunities/{primaryId}/engagements",
			new { message = "Signing up for issue 759 detail enrichment coverage." });
		engagementResponse.EnsureSuccessStatusCode();

		await Page.GotoAsync($"{origin}/volunteer-opportunities/{primaryId}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Expect(Page.GetByText(new Regex("posted", RegexOptions.IgnoreCase)))
			.ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(Page.GetByText(new Regex(@"1\s+person", RegexOptions.IgnoreCase)))
			.ToBeVisibleAsync(new() { Timeout = 15_000 });

		var aboutOrg = Page.GetByTestId("about-organization");
		await Expect(aboutOrg).ToBeVisibleAsync();
		await Expect(aboutOrg.GetByRole(AriaRole.Link, new() { Name = contactEmail }))
			.ToBeVisibleAsync();
		await Expect(aboutOrg.GetByText("Teststrasse 1, 12345 Musterstadt")).ToBeVisibleAsync();

		var teaser = Page.GetByTestId("more-from-organization");
		await Expect(teaser).ToBeVisibleAsync();
		await Expect(teaser.Locator("li")).ToHaveCountAsync(3);
		await Expect(teaser.GetByText(primaryTitle)).Not.ToBeVisibleAsync();
		await Expect(teaser.GetByText(others[0].Title)).Not.ToBeVisibleAsync();
		foreach (var other in others.Skip(1))
			await Expect(teaser.GetByText(other.Title)).ToBeVisibleAsync();
	}

	[Test]
	public async Task EditWizard_ReopenedDraft_ShowsSaveAsDraftAndAcceptsPartialSave()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var uniqueTitle = $"Edit Draft Visual Test {Guid.NewGuid().ToString("N")[..8]}";

		var pinnedOrgId = await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "olaf", "olaf123");
		await AuthHelper.GoToOrgAppDashboardAsync(Page, frontend, pinnedOrgId!.Value);

		var createBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Create opportunity" });
		await Expect(createBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await createBtn.First.ClickAsync();

		await Page.WaitForSelectorAsync("[role='dialog']", new() { Timeout = 5000 });

		await Page.Locator("#opportunity-title").FillAsync(uniqueTitle);
		await Page.GetByTestId("modal-save-draft").ClickAsync();

		await Expect(Page).ToHaveURLAsync(new Regex(@"/opportunities"), new() { Timeout = 30_000 });

		var draftsSection = Page.GetByTestId("drafts-section");
		await Expect(draftsSection).ToBeVisibleAsync();

		var draftRow = draftsSection.Locator("li", new() { HasText = uniqueTitle });
		await Expect(draftRow).ToBeVisibleAsync();
		await OpportunityRowHelper.ClickActionAsync(draftRow, "opportunity-edit");

		await Page.WaitForSelectorAsync("[role='dialog']", new() { Timeout = 10_000 });

		var saveDraftBtn = Page.GetByTestId("modal-save-draft");
		await Expect(saveDraftBtn).ToBeVisibleAsync();

		var updatedTitle = $"{uniqueTitle} Updated";
		await Page.Locator("#opportunity-title").FillAsync(updatedTitle);
		await saveDraftBtn.ClickAsync();

		await Expect(Page.Locator("[role='dialog']")).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(draftsSection.GetByText(updatedTitle)).ToBeVisibleAsync(new() { Timeout = 15_000 });
	}

	[Test]
	public async Task PublishScheduledSlots_BlockedWithNoTimeSlots_SucceedsAfterAddingOne()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var uniqueTitle = $"ScheduledSlots Publish Gap Test {Guid.NewGuid().ToString("N")[..8]}";

		var pinnedOrgId = await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "olaf", "olaf123");
		await AuthHelper.GoToOrgAppDashboardAsync(Page, frontend, pinnedOrgId!.Value);

		var createBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Create opportunity" });
		await Expect(createBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await createBtn.First.ClickAsync();

		await Page.WaitForSelectorAsync("[role='dialog']", new() { Timeout = 5000 });

		await Page.Locator("#opportunity-title").FillAsync(uniqueTitle);
		await Page.Locator("#opportunity-description").FillAsync(
			"Regression test for the ScheduledSlots publish-with-no-slots gap.");

		await Page.GetByTestId("wizard-stepper-2").ClickAsync();
		await Page.Locator("#opportunity-remote").CheckAsync();

		await Page.GetByTestId("wizard-stepper-3").ClickAsync();
		await Page.Locator("label:has(input[name='participationType'][value='ScheduledSlots'])").ClickAsync();

		await Page.GetByTestId("wizard-stepper-4").ClickAsync();
		await Page.GetByTestId("modal-submit").ClickAsync();
		await Expect(Page.Locator("[role='dialog']")).ToBeVisibleAsync();
		await Expect(Page.GetByTestId("wizard-step-4")).ToBeVisibleAsync();

		var publishError = Page.GetByRole(AriaRole.Alert)
			.Filter(new() { HasTextString = "time slot" });
		await Expect(publishError).ToBeVisibleAsync();
		await Expect(publishError).ToBeInViewportAsync();
		await Expect(publishError).ToBeFocusedAsync();

		var start = DateTimeOffset.UtcNow.AddDays(7);
		var end = start.AddHours(2);
		var step4 = Page.GetByTestId("wizard-step-4");
		await step4.GetByRole(AriaRole.Button, new() { Name = "Add time slot", Exact = true }).ClickAsync();
		var slotEditor = Page.GetByTestId("time-slot-editor");
		await FillDateTimePickerAsync(slotEditor, "slot-start", start);
		await FillDateTimePickerAsync(slotEditor, "slot-end", end);
		await slotEditor.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
		await Expect(slotEditor).Not.ToBeVisibleAsync();

		await Expect(step4.GetByText("No time slots added yet.")).Not.ToBeVisibleAsync(
			new() { Timeout = 5000 });

		await Page.GetByTestId("modal-submit").ClickAsync();
		await Expect(Page.Locator("[role='dialog']")).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

		await Page.GotoAsync($"{frontend.GetLeftPart(UriPartial.Authority)}/opportunities");
		await Expect(Page.Locator("main")).ToBeVisibleAsync(new() { Timeout = 15_000 });
		var listedCard = Page
			.Locator("ul li:has(a[href*='/volunteer-opportunities/'])")
			.Filter(new() { HasText = uniqueTitle });
		await Expect(listedCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
	}

	[Test]
	public async Task DetailPage_ClearsStaleError_AfterClientSideNavigationToAnotherOpportunity()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var suffix = Guid.NewGuid().ToString("N")[..8];

		var olafToken = (await Fixture.SignInAsync("olaf", "olaf123")).AccessToken;
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafToken}");

		var orgResponse = await PostJsonWithRetryAsync(http, "/v1/organizations", new { name = $"Stale Error Org {suffix}" });
		orgResponse.EnsureSuccessStatusCode();
		var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = org.GetProperty("id").GetProperty("value").GetString();

		async Task<(string Id, string Title)> CreateOpportunityAsync(string label)
		{
			var title = $"{label} {suffix}";
			var response = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
			{
				titleDe = title,
				descriptionDe = $"{label} opportunity for issue 1223 stale-error coverage.",
				organizationId,
				isRemote = true,
				occurrence = "OneTime",
				participationType = "IndividualContact",
				checkInMethod = "None",
				validUntil = DateTimeOffset.UtcNow.AddDays(30),
				isDraft = false,
			});
			response.EnsureSuccessStatusCode();
			var body = await response.Content.ReadFromJsonAsync<JsonElement>();
			return (body.GetProperty("id").GetString()!, title);
		}

		var (idA, titleA) = await CreateOpportunityAsync("Sibling A");
		var (idB, titleB) = await CreateOpportunityAsync("Sibling B");

		await Page.GotoAsync($"{origin}/volunteer-opportunities/{idA}");
		await Expect(Page.Locator("h1").First).ToHaveTextAsync(titleA, new() { Timeout = 15_000 });

		var teaser = Page.GetByTestId("more-from-organization");
		var siblingLink = teaser.GetByRole(AriaRole.Link, new() { Name = titleB });
		await Expect(siblingLink).ToBeVisibleAsync();

		var failedOnce = false;
		await Page.RouteAsync($"**/v1/volunteer-opportunities/{idB}", async route =>
		{
			if (failedOnce)
			{
				await route.ContinueAsync();
				return;
			}
			failedOnce = true;
			await route.FulfillAsync(new()
			{
				Status = 500,
				ContentType = "application/json",
				Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
				Body = "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"status\":500}",
			});
		});

		await siblingLink.ClickAsync();
		var errorBanner = Page.Locator("[role='alert'][aria-live='assertive']");
		await Expect(errorBanner).ToBeVisibleAsync(new() { Timeout = 10_000 });

		await Page.GoBackAsync();
		await Expect(Page.Locator("h1").First).ToHaveTextAsync(titleA, new() { Timeout = 15_000 });
		await Expect(errorBanner).Not.ToBeVisibleAsync();
	}

	[Test]
	public async Task DetailPage_OwnerViewingOwnPublishedOpportunity_HidesDraftBadgeAndPublishEditActions()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var olafToken = (await Fixture.SignInAsync("olaf", "olaf123")).AccessToken;
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafToken}");

		var suffix = Guid.NewGuid().ToString("N")[..8];
		var orgResponse = await PostJsonWithRetryAsync(http, "/v1/organizations", new { name = $"Detail Published Org {suffix}" });
		orgResponse.EnsureSuccessStatusCode();
		var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = org.GetProperty("id").GetProperty("value").GetString();

		var publishedTitle = $"Detail Published Test {suffix}";
		var response = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
		{
			titleDe = publishedTitle,
			descriptionDe = "Seeded published opportunity for the detail-page owner-affordances edge case.",
			organizationId,
			isRemote = true,
			occurrence = "OneTime",
			participationType = "IndividualContact",
			checkInMethod = "None",
			validUntil = DateTimeOffset.UtcNow.AddDays(30),
			isDraft = false,
		});
		response.EnsureSuccessStatusCode();
		var opportunity = await response.Content.ReadFromJsonAsync<JsonElement>();
		var opportunityId = opportunity.GetProperty("id").GetString();

		await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "olaf", "olaf123");
		await Page.GotoAsync($"{origin}/volunteer-opportunities/{opportunityId}");
		await Expect(Page.Locator("h1").First).ToHaveTextAsync(publishedTitle, new() { Timeout = 15_000 });

		await Expect(Page.GetByTestId("opportunity-detail-draft-badge")).Not.ToBeVisibleAsync();
		await Expect(Page.GetByTestId("opportunity-detail-edit")).Not.ToBeVisibleAsync();
		await Expect(Page.GetByTestId("opportunity-detail-publish")).Not.ToBeVisibleAsync();
	}

	[Test]
	public async Task ListCard_TagChips_AreClickableLinks_SwitchTagFilterAndSurviveSpecialCharacters()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var suffix = Guid.NewGuid().ToString("N")[..8];

		var olafToken = (await Fixture.SignInAsync("olaf", "olaf123")).AccessToken;
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafToken}");

		var orgResponse = await PostJsonWithRetryAsync(http, "/v1/organizations", new { name = $"List Tag Chip Org {suffix}" });
		orgResponse.EnsureSuccessStatusCode();
		var org = await orgResponse.Content.ReadFromJsonAsync<JsonElement>();
		var organizationId = org.GetProperty("id").GetProperty("value").GetString();

		var tagA = $"listtagchip-{suffix}";
		var tagB = $"list tag & chip {suffix}";
		var title = $"List Tag Chip Opportunity {suffix}";
		var oppResponse = await http.PostAsJsonAsync("/v1/volunteer-opportunities", new
		{
			titleDe = title,
			descriptionDe = "Created by ListCard_TagChips_AreClickableLinks_SwitchTagFilterAndSurviveSpecialCharacters",
			organizationId,
			isRemote = true,
			occurrence = "OneTime",
			participationType = "IndividualContact",
			checkInMethod = "None",
			validUntil = DateTimeOffset.UtcNow.AddDays(30),
			isDraft = false,
			tags = new[] { tagA, tagB },
		});
		oppResponse.EnsureSuccessStatusCode();

		await Page.GotoAsync($"{origin}/opportunities?tag={Uri.EscapeDataString(tagA)}");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		var card = Page.Locator("ul li:has(a[href*='/volunteer-opportunities/'])").Filter(new() { HasText = title });
		await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

		await Expect(card.GetByRole(AriaRole.Link, new() { Name = $"Filter by tag: {tagA}" })).ToBeVisibleAsync();
		var tagBChip = card.GetByRole(AriaRole.Link, new() { Name = $"Filter by tag: {tagB}" });
		await Expect(tagBChip).ToBeVisibleAsync();

		await tagBChip.ClickAsync();
		await Expect(Page).ToHaveURLAsync(
			new Regex($"^{Regex.Escape($"{origin}/opportunities?tag={Uri.EscapeDataString(tagB)}")}$"),
			new() { Timeout = 15_000 });
		await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
	}
}
