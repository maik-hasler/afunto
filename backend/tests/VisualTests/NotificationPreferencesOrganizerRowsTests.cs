using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("visualtests-db")]
public class NotificationPreferencesOrganizerRowsTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	[Before(Test)]
	public Task ResetVisualTestStateAsync() => Fixture.ResetAsync();

	[Test]
	public async Task Profile_SaveWithHiddenOrganizerRows_StillSendsTheirStoredValues()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);
		var backend = Fixture.GetEndpoint("backend");

		var vera = await Fixture.SignInAsync("vera", "vera123");
		using var http = new HttpClient { BaseAddress = backend };
		http.DefaultRequestHeaders.Add("Authorization", $"Bearer {vera.AccessToken}");

		var original = await GetPreferencesAsync(http);
		await PutPreferencesAsync(http, original with { NotifyOnNewSignUp = true, NotifyOnWithdrawal = true });

		try
		{
			string? savedPayload = null;
			await Page.RouteAsync("**/v1/users/me/notification-preferences", async route =>
			{
				if (route.Request.Method == "PUT")
				{
					savedPayload = route.Request.PostData;
				}

				await route.ContinueAsync();
			});

			await AuthHelper.FastSignInAsync(Page, Fixture, frontend, "vera", "vera123");
			await Page.GotoAsync($"{origin}/profile");
			await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

			await Expect(Page.Locator("#notifyOnEngagementConfirmed"))
				.ToBeVisibleAsync(new() { Timeout = 20_000 });
			await Expect(Page.Locator("#notifyOnNewSignUp")).ToHaveCountAsync(0);

			// The preferences are read-only until the page's own profile edit
			// mode is entered - there is no longer a save button of their own
			// (#2354).
			await Page.GetByTestId("profile-edit").ClickAsync();
			await Page.GetByTestId("profile-save").ClickAsync();
			await Expect(Page.GetByText("Profile saved.")).ToBeVisibleAsync(
				new() { Timeout = 15_000 });

			savedPayload.Should().NotBeNull("the Save button should have issued a PUT");
			var sent = JsonSerializer.Deserialize<NotificationPreferences>(
				savedPayload ?? "null", new JsonSerializerOptions(JsonSerializerDefaults.Web))
				?? throw new InvalidOperationException($"Unexpected PUT payload: {savedPayload}");
			sent.NotifyOnNewSignUp.Should().BeTrue(
				"a hidden organizer preference must be sent back with the value the server returned");
			sent.NotifyOnWithdrawal.Should().BeTrue(
				"a hidden organizer preference must be sent back with the value the server returned");

			var persisted = await GetPreferencesAsync(http);
			persisted.NotifyOnNewSignUp.Should().BeTrue();
			persisted.NotifyOnWithdrawal.Should().BeTrue();
		}
		finally
		{
			await PutPreferencesAsync(http, original);
		}
	}

	private static async Task<NotificationPreferences> GetPreferencesAsync(HttpClient http)
	{
		var response = await http.GetAsync("/v1/users/me/notification-preferences");
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<NotificationPreferences>()
			?? throw new InvalidOperationException("Notification preferences response was empty.");
	}

	private static async Task PutPreferencesAsync(HttpClient http, NotificationPreferences preferences)
	{
		var response = await http.PutAsJsonAsync("/v1/users/me/notification-preferences", preferences);
		response.EnsureSuccessStatusCode();
	}

	private sealed record NotificationPreferences(
		bool NotifyOnNewSignUp,
		bool NotifyOnWithdrawal,
		bool NotifyOnEngagementConfirmed,
		bool NotifyOnEngagementReminder);
}
