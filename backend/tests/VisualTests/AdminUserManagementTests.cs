using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace VisualTests;

[ClassDataSource<AspireFixture>(Shared = SharedType.PerTestSession)]
public class AdminUserManagementTests(AspireFixture fixture) : VisualTestBase(fixture)
{
	private const string Realm = "afunto";
	private const int MobileWidth = 390;
	private const int MobileHeight = 844;

	[Test]
	public async Task AdministrationPage_BlockAndPromote_UpdatesRowState()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var (username, userId) = await CreateDisposableUserAsync(keycloak);
		try
		{
			await AuthHelper.LoginAsync(Page, frontend, "admin", "admin123");
			await Page.GotoAsync($"{origin}/administration/users");
			await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

			await Page.Locator("#admin-user-search").FillAsync(username);

			await Page.Locator("form")
				.Filter(new() { Has = Page.Locator("#admin-user-search") })
				.GetByRole(AriaRole.Button, new() { Name = "Search" })
				.ClickAsync();

			var row = Page.Locator("li").Filter(new() { HasTextString = username });
			await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
			await Expect(row.GetByText("Active")).ToBeVisibleAsync();

			await row.GetByRole(AriaRole.Button, new() { Name = $"Block {username}" }).ClickAsync();
			await ConfirmDialogAsync("Yes, block");
			await Expect(row.GetByText("Blocked")).ToBeVisibleAsync();
			await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Unblock {username}" })).ToBeVisibleAsync();

			await row.GetByRole(AriaRole.Button, new() { Name = $"Promote {username} to admin" }).ClickAsync();
			await ConfirmDialogAsync("Yes, promote");
			var adminBadge = row.GetByText("Admin", new() { Exact = true });
			await Expect(adminBadge).ToBeVisibleAsync();
			await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Remove admin from {username}" })).ToBeVisibleAsync();

			var badgeBackground = await adminBadge.EvaluateAsync<string>(
				"el => getComputedStyle(el).backgroundColor");
			badgeBackground.Should().Be("rgb(240, 250, 245)", "the admin badge should use the brand tone, not amber");
		}
		finally
		{
			await DeleteKeycloakUserAsync(keycloak, userId);
		}
	}

	[Test]
	public async Task AdministrationPage_BlockAndPromote_RequireConfirmationNamingTheUser()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var (username, userId) = await CreateDisposableUserAsync(keycloak);
		try
		{
			await AuthHelper.LoginAsync(Page, frontend, "admin", "admin123");
			await Page.GotoAsync($"{origin}/administration/users");
			await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

			await Page.Locator("#admin-user-search").FillAsync(username);
			await Page.Locator("form")
				.Filter(new() { Has = Page.Locator("#admin-user-search") })
				.GetByRole(AriaRole.Button, new() { Name = "Search" })
				.ClickAsync();

			var row = Page.Locator("li").Filter(new() { HasTextString = username });
			await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
			await Expect(row.GetByText("Active")).ToBeVisibleAsync();

			var dialog = Page.GetByRole(AriaRole.Dialog);

			await row.GetByRole(AriaRole.Button, new() { Name = $"Block {username}" }).ClickAsync();
			await Expect(dialog).ToBeVisibleAsync();
			await Expect(dialog.GetByText(username)).ToBeVisibleAsync();
			await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep" }).ClickAsync();
			await Expect(dialog).Not.ToBeVisibleAsync();

			await Expect(row.GetByText("Active")).ToBeVisibleAsync();
			(await IsUserEnabledAsync(keycloak, userId)).Should()
				.BeTrue("dismissing the block confirmation must not block the account");

			await row.GetByRole(AriaRole.Button, new() { Name = $"Promote {username} to admin" }).ClickAsync();
			await Expect(dialog).ToBeVisibleAsync();
			await Expect(dialog.GetByText(username)).ToBeVisibleAsync();
			// "Cancel", not the "Keep" the block dialog above uses: granting admin rights is
			// constructive, so the dialog drops the label that only reads right against a
			// removal - and with it the red confirm button (#2326).
			await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
			await Expect(dialog).Not.ToBeVisibleAsync();

			await Expect(row.GetByText("Admin", new() { Exact = true })).Not.ToBeVisibleAsync();
			(await HasAdminRealmRoleAsync(keycloak, userId)).Should()
				.BeFalse("dismissing the promote confirmation must not grant platform admin");
		}
		finally
		{
			await DeleteKeycloakUserAsync(keycloak, userId);
		}
	}

	[Test]
	public async Task AdministrationPage_MobileViewport_UserRowStacksNameAboveActions()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var keycloak = Fixture.GetEndpoint("keycloak");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		var (username, userId) = await CreateDisposableUserAsync(keycloak);
		try
		{
			await AuthHelper.LoginAsync(Page, frontend, "admin", "admin123");
			await Page.SetViewportSizeAsync(MobileWidth, MobileHeight);
			await Page.GotoAsync($"{origin}/administration/users");
			await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

			await Page.Locator("#admin-user-search").FillAsync(username);

			await Page.Locator("form")
				.Filter(new() { Has = Page.Locator("#admin-user-search") })
				.GetByRole(AriaRole.Button, new() { Name = "Search" })
				.ClickAsync();

			var row = Page.Locator("li").Filter(new() { HasTextString = username });
			await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });

			var nameCell = row.Locator("p").First;
			var blockButton = row.GetByRole(AriaRole.Button, new() { Name = $"Block {username}" });
			await Expect(blockButton).ToBeVisibleAsync();

			var nameWidth = 0f;
			var nameBottom = 0f;
			var blockY = 0f;
			await PollUntilAsync(async () =>
			{
				var nameBox = await nameCell.BoundingBoxAsync();
				var blockBox = await blockButton.BoundingBoxAsync();
				if (nameBox is null || blockBox is null)
					return false;

				nameWidth = nameBox.Width;
				nameBottom = nameBox.Y + nameBox.Height;
				blockY = blockBox.Y;
				return nameWidth > 200f && blockY >= nameBottom;
			}, () => $"Name cell width ({nameWidth:F0}px, want >200px - should span most of the "
				+ $"{MobileWidth}px viewport, not be compressed next to the action buttons) / "
				+ $"Block button Y ({blockY:F0}px, want >= name-cell-bottom {nameBottom:F0}px - "
				+ "should stack below the name/email text on narrow viewports, not sit beside it)",
				timeoutMs: 10_000);
		}
		finally
		{
			await DeleteKeycloakUserAsync(keycloak, userId);
		}
	}

	[Test]
	public async Task AdministrationPage_OwnRow_HasNoBlockOrDemoteButtons()
	{
		var frontend = Fixture.GetEndpoint("frontend");
		var origin = frontend.GetLeftPart(UriPartial.Authority);

		await AuthHelper.LoginAsync(Page, frontend, "admin", "admin123");
		await Page.GotoAsync($"{origin}/administration/users");
		await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

		await Page.Locator("#admin-user-search").FillAsync("admin");

		await Page.Locator("form")
			.Filter(new() { Has = Page.Locator("#admin-user-search") })
			.GetByRole(AriaRole.Button, new() { Name = "Search" })
			.ClickAsync();

		var ownRow = Page.Locator("li").Filter(new() { HasTextString = "admin@example.com" });
		await Expect(ownRow).ToBeVisibleAsync(new() { Timeout = 15_000 });
		await Expect(ownRow.GetByText("You cannot change your own account here.")).ToBeVisibleAsync();
		await Expect(ownRow.GetByRole(AriaRole.Button)).Not.ToBeVisibleAsync();
	}

	private async Task ConfirmDialogAsync(string confirmLabel)
	{
		var dialog = Page.GetByRole(AriaRole.Dialog);
		await Expect(dialog).ToBeVisibleAsync();
		await dialog.GetByRole(AriaRole.Button, new() { Name = confirmLabel }).ClickAsync();
		await Expect(dialog).Not.ToBeVisibleAsync();
	}

	private static async Task<bool> IsUserEnabledAsync(Uri keycloak, string userId)
	{
		var adminToken = await AuthHelper.GetAdminTokenAsync(keycloak);

		using var adminHttp = new HttpClient { BaseAddress = keycloak };
		adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await adminHttp.GetAsync($"/admin/realms/{Realm}/users/{userId}");
		response.EnsureSuccessStatusCode();
		var user = await response.Content.ReadFromJsonAsync<JsonElement>();
		return user.GetProperty("enabled").GetBoolean();
	}

	private static async Task<bool> HasAdminRealmRoleAsync(Uri keycloak, string userId)
	{
		var adminToken = await AuthHelper.GetAdminTokenAsync(keycloak);

		using var adminHttp = new HttpClient { BaseAddress = keycloak };
		adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await adminHttp.GetAsync($"/admin/realms/{Realm}/users/{userId}/role-mappings/realm");
		response.EnsureSuccessStatusCode();
		var roles = await response.Content.ReadFromJsonAsync<JsonElement>();
		return roles.EnumerateArray()
			.Any(role => role.GetProperty("name").GetString() == "admin");
	}

	private static async Task<(string Username, string UserId)> CreateDisposableUserAsync(Uri keycloak)
	{
		var adminToken = await AuthHelper.GetAdminTokenAsync(keycloak);

		var username = $"tempuser760-{Guid.NewGuid():N}";

		using var adminHttp = new HttpClient { BaseAddress = keycloak };
		adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var createResponse = await adminHttp.PostAsJsonAsync($"/admin/realms/{Realm}/users", new
		{
			username,
			email = $"{username}@example.test",
			enabled = true,
			emailVerified = true,
			credentials = new[] { new { type = "password", value = $"Test760!{Guid.NewGuid():N}", temporary = false } },
		});
		createResponse.EnsureSuccessStatusCode();
		var userId = createResponse.Headers.Location!.Segments[^1];

		var roleResponse = await adminHttp.GetAsync($"/admin/realms/{Realm}/roles/user");
		roleResponse.EnsureSuccessStatusCode();
		var role = await roleResponse.Content.ReadFromJsonAsync<JsonElement>();

		var assignRoleResponse = await adminHttp.PostAsJsonAsync(
			$"/admin/realms/{Realm}/users/{userId}/role-mappings/realm",
			new[] { new { id = role.GetProperty("id").GetString(), name = role.GetProperty("name").GetString() } });
		assignRoleResponse.EnsureSuccessStatusCode();

		return (username, userId);
	}

	private static async Task DeleteKeycloakUserAsync(Uri keycloak, string userId)
	{
		var adminToken = await AuthHelper.GetAdminTokenAsync(keycloak);

		using var adminHttp = new HttpClient { BaseAddress = keycloak };
		adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await adminHttp.DeleteAsync($"/admin/realms/{Realm}/users/{userId}");
		response.EnsureSuccessStatusCode();
	}
}
