using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class SelfRegistrationDefaultRoleTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	private const string Realm = "afunto";

	[Test]
	public async Task Register_CompletesForm_GrantsUserRoleForAuthenticatedApiCalls()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var backend = Fixture.GetEndpoint("backend");
		var suffix = Guid.NewGuid().ToString("N");
		var username = $"selfreg-{suffix}";
		var email = $"{username}@example.test";
		const string password = "Selfreg1!";

		await AuthHelper.AllowKeycloakCrossOriginRequestsAsync(Page);
		await Page.GotoAsync(frontend.ToString());
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
		await Page.GetByRole(AriaRole.Button, new() { Name = "Register" }).First.ClickAsync();

		await Expect(Page.Locator("#kc-register-form")).ToBeVisibleAsync(new() { Timeout = 30_000 });
		await Page.Locator("#email").FillAsync(email);
		await Page.Locator("#username").FillAsync(username);
		await Page.Locator("#termsAccepted").CheckAsync();
		await Page.Locator("#kc-register-form input[type=submit]").ClickAsync();

		await Expect(Page.Locator("#kc-register-form")).Not.ToBeVisibleAsync(new() { Timeout = 30_000 });

		using var adminHttp = new HttpClient { BaseAddress = keycloak };
		adminHttp.DefaultRequestHeaders.Add("Authorization", $"Bearer {await AuthHelper.GetAdminTokenAsync(keycloak)}");

		var userId = await FindUserIdAsync(adminHttp, username);
		try
		{
			await SetPasswordAndClearRequiredActionsAsync(adminHttp, userId, password);

			var accessToken = await AuthHelper.GetTokenAsync(keycloak, username, password);

			using var http = new HttpClient { BaseAddress = backend };
			http.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
			var response = await http.GetAsync("/v1/users/me");

			if (!response.IsSuccessStatusCode)
				throw new Exception(
					$"Self-registered user '{username}' got {(int)response.StatusCode} from "
					+ "GET /v1/users/me - the realm's default role is missing 'user' (#1723).");
		}
		finally
		{
			await adminHttp.DeleteAsync($"/admin/realms/{Realm}/users/{userId}");
		}
	}

	private static async Task<string> FindUserIdAsync(HttpClient adminHttp, string username)
	{
		var response = await adminHttp.GetAsync($"/admin/realms/{Realm}/users?username={username}&exact=true");
		response.EnsureSuccessStatusCode();
		var users = await response.Content.ReadFromJsonAsync<JsonElement>();
		var first = users.EnumerateArray().FirstOrDefault();
		if (first.ValueKind != JsonValueKind.Object)
			throw new Exception($"Registration did not create a Keycloak user for username '{username}'.");

		return first.GetProperty("id").GetString()!;
	}

	private static async Task SetPasswordAndClearRequiredActionsAsync(
		HttpClient adminHttp, string userId, string password)
	{
		var updateResponse = await adminHttp.PutAsJsonAsync($"/admin/realms/{Realm}/users/{userId}", new
		{
			emailVerified = true,
			requiredActions = Array.Empty<string>(),
		});
		updateResponse.EnsureSuccessStatusCode();

		var resetPasswordResponse = await adminHttp.PutAsJsonAsync(
			$"/admin/realms/{Realm}/users/{userId}/reset-password",
			new { type = "password", value = password, temporary = false });
		resetPasswordResponse.EnsureSuccessStatusCode();
	}
}
