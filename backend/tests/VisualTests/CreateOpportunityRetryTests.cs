using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class CreateOpportunityRetryTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Test]
	public async Task PublishScheduledSlots_TimeSlotFailureMidPublish_RetryUpdatesSameDraftInsteadOfDuplicating()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var backend = Fixture.GetEndpoint("backend");
		var uniqueTitle = $"Retry Dedup Test {Guid.NewGuid().ToString("N")[..8]}";

		var pinnedOrgId = await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "olaf", "olaf123");
		var organizationId = pinnedOrgId!.Value;
		await AuthHelper.GoToOrgAppDashboardAsync(Page, frontend, organizationId);

		var createBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Create opportunity" });
		await Expect(createBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await createBtn.First.ClickAsync();

		await Page.WaitForSelectorAsync("[role='dialog']", new() { Timeout = 5000 });

		await Page.Locator("#opportunity-title").FillAsync(uniqueTitle);
		await Page.Locator("#opportunity-description").FillAsync(
			"Regression test for the #1227 retry-duplicate-draft bug.");

		await Page.GetByTestId("wizard-stepper-2").ClickAsync();
		await Page.Locator("label:has(#opportunity-remote)").ClickAsync();

		await Page.GetByTestId("wizard-stepper-3").ClickAsync();
		await Page.Locator("label:has(input[name='participationType'][value='ScheduledSlots'])").ClickAsync();

		var scheduleStep = Page.GetByTestId("wizard-step-3");

		async Task AddSlotAsync(int daysFromNow)
		{
			var start = DateTimeOffset.UtcNow.AddDays(daysFromNow);
			var end = start.AddHours(2);
			await scheduleStep.GetByRole(AriaRole.Button, new() { Name = "Add time slot", Exact = true }).ClickAsync();
			var slotEditor = Page.GetByTestId("time-slot-editor");
			await FillDateTimePickerAsync(slotEditor, "slot-start", start);
			await FillDateTimePickerAsync(slotEditor, "slot-end", end);
			await slotEditor.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
			await Expect(slotEditor).Not.ToBeVisibleAsync();
		}

		await AddSlotAsync(7);
		await AddSlotAsync(14);
		await Expect(scheduleStep.Locator("ul li")).ToHaveCountAsync(2);

		var timeSlotCallCount = 0;
		await Page.RouteAsync("**/volunteer-opportunities/*/time-slots", async route =>
		{
			if (route.Request.Method != "POST")
			{
				await route.ContinueAsync();
				return;
			}

			timeSlotCallCount++;
			if (timeSlotCallCount != 1)
			{
				await route.ContinueAsync();
				return;
			}

			await route.FulfillAsync(new()
			{
				Status = 500,
				ContentType = "application/json",
				Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
				Body = "{\"type\":\"about:blank\",\"status\":500,\"title\":\"Internal Server Error\"}",
			});
		});

		await Page.GetByTestId("modal-submit").ClickAsync();

		var errorBanner = Page.GetByRole(AriaRole.Alert)
			.Filter(new() { HasTextString = "Unknown error" });
		await Expect(errorBanner).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(Page.Locator("[role='dialog']")).ToBeVisibleAsync();

		var olafToken = (await Fixture.SignInAsync("olaf", "olaf123")).AccessToken;
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {olafToken}");

		async Task<List<JsonElement>> GetOpportunitiesByTitleAsync(string status)
		{
			var response = await http.GetAsync(
				$"/v1/organizations/{organizationId}/opportunities?status={status}&pageNumber=1&pageSize=100");
			response.EnsureSuccessStatusCode();
			var body = await response.Content.ReadFromJsonAsync<JsonElement>();
			return body.GetProperty("items").EnumerateArray()
				.Where(o => o.GetProperty("titleDe").GetString() == uniqueTitle)
				.ToList();
		}

		var draftsAfterFailure = await GetOpportunitiesByTitleAsync("Draft");
		draftsAfterFailure.Should().HaveCount(1,
			"the failed time-slot creation must leave exactly the one draft created before it failed");
		var opportunityId = draftsAfterFailure[0].GetProperty("id").GetString();

		await Page.GetByTestId("modal-submit").ClickAsync();
		await Expect(Page.Locator("[role='dialog']")).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

		var draftsAfterRetry = await GetOpportunitiesByTitleAsync("Draft");
		draftsAfterRetry.Should().BeEmpty(
			"the retried publish must move the original draft to Published, not leave a second draft behind");

		var publishedAfterRetry = await GetOpportunitiesByTitleAsync("Published");
		publishedAfterRetry.Should().HaveCount(1,
			"retrying publish must reuse the original draft's id instead of creating a duplicate opportunity");
		publishedAfterRetry[0].GetProperty("id").GetString().Should().Be(opportunityId,
			"the published opportunity must be the same one created on the first attempt");

		var detailsResponse = await http.GetAsync($"/v1/volunteer-opportunities/{opportunityId}");
		detailsResponse.EnsureSuccessStatusCode();
		var details = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
		details.GetProperty("timeSlots").GetArrayLength().Should().Be(2,
			"both time slots must exist exactly once - the one that failed on the first attempt, "
			+ "created on retry, plus the one that never failed");
	}
}
