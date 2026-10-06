using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

using HttpStatusCode = System.Net.HttpStatusCode;

namespace VisualTests;

public static class AuthHelper
{
	private const string FrontendClientId = "frontend";

	private const string Realm = "afunto";
	private const string FrontendTestClientId = "frontend-test";
	private const string BackendClientId = "backend";
	private const string BackendClientSecret = "backend-secret";

	public static async Task<string> GetTokenAsync(Uri keycloak, string username, string password)
	{
		using var http = new HttpClient { BaseAddress = keycloak };
		using var response = await PostTokenRequestWithRetryAsync(
			http,
			$"/realms/{Realm}/protocol/openid-connect/token",
			() => new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "password",
				["client_id"] = FrontendTestClientId,
				["username"] = username,
				["password"] = password,
				["scope"] = "openid",
			}));
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		return body.GetProperty("access_token").GetString()
			?? throw new InvalidOperationException("Keycloak returned no access_token.");
	}

	public static async Task<string> GetAdminTokenAsync(Uri keycloak)
	{
		using var http = new HttpClient { BaseAddress = keycloak };
		using var response = await PostTokenRequestWithRetryAsync(
			http,
			$"/realms/{Realm}/protocol/openid-connect/token",
			() => new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = BackendClientId,
				["client_secret"] = BackendClientSecret,
			}));
		response.EnsureSuccessStatusCode();

		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		return body.GetProperty("access_token").GetString()
			?? throw new InvalidOperationException("Keycloak returned no access_token.");
	}

	public static async Task<HttpResponseMessage> PostTokenRequestWithRetryAsync(
		HttpClient client, string requestUri, Func<FormUrlEncodedContent> contentFactory,
		CancellationToken cancellationToken = default)
	{
		const int maxAttempts = 4;
		HttpResponseMessage response;
		for (var attempt = 1; ; attempt++)
		{
			using var content = contentFactory();
			response = await client.PostAsync(requestUri, content, cancellationToken);
			if (response.StatusCode < HttpStatusCode.InternalServerError || attempt >= maxAttempts)
				break;

			response.Dispose();
			await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);
		}

		return response;
	}

	public static async Task LoginAsync(IPage page, Uri frontendUrl, string username, string password)
	{
		await AllowKeycloakCrossOriginRequestsAsync(page);

		try
		{
			await DriveLoginAsync(page, frontendUrl, username, password);
			return;
		}

		catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
		{
			// Fall through to the single retry below.
		}

		await page.GotoAsync(frontendUrl.ToString());
		try
		{
			await page.GetByRole(AriaRole.Button, new() { Name = "User menu" })
				.WaitForAsync(new() { Timeout = 15_000 });
			return;
		}
		catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
		{
			// Genuinely not signed in - drive the form again.
		}

		await DriveLoginAsync(page, frontendUrl, username, password);
	}

	private static async Task DriveLoginAsync(IPage page, Uri frontendUrl, string username, string password)
	{
		await page.GotoAsync(frontendUrl.ToString());

		await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).First.ClickAsync();

		await page.Locator("#username").WaitForAsync(new() { Timeout = 30_000 });

		await page.Locator("#username").FillAsync(username);
		await page.Locator("#password").FillAsync(password);
		await page.Locator("#kc-login").ClickAsync();

		await page.GetByRole(AriaRole.Button, new() { Name = "User menu" })
			.WaitForAsync(new() { Timeout = 45_000 });
	}

	public static Task AllowKeycloakCrossOriginRequestsAsync(IPage page) =>
		page.RouteAsync("**/realms/**", async route =>
		{
			var headers = new Dictionary<string, string>(route.Request.Headers, StringComparer.OrdinalIgnoreCase);
			headers.Remove("X-Forwarded-For");
			await route.ContinueAsync(new() { Headers = headers });
		});

	public static async Task<Guid?> FastSignInAsync(
		IPage page, AspireFixture fixture, Uri frontendUrl, string username, string password,
		bool pinActiveOrg = true)
	{
		var session = await fixture.SignInAsync(username, password);

		var profile = DecodeJwtPayload(session.IdToken);
		var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + session.ExpiresIn;

		var storageValue = JsonSerializer.Serialize(new
		{
			id_token = session.IdToken,
			session_state = (string?)null,
			access_token = session.AccessToken,

			refresh_token = (string?)null,
			token_type = session.TokenType,
			scope = "openid",
			profile,
			expires_at = expiresAt,
		});
		var storageKey = $"oidc.user:{session.Authority}:{FrontendClientId}";

		await page.AddInitScriptAsync(
			$"window.sessionStorage.setItem({JsonSerializer.Serialize(storageKey)}, "
			+ $"{JsonSerializer.Serialize(storageValue)});");

		var pinnedOrganizationId = fixture.GetPinnedOrganizerOrganizationId(session.UserId);

		if (pinActiveOrg && pinnedOrganizationId is { } organizationId)
		{
			await page.Context.AddCookiesAsync([
				new Cookie
				{
					Name = "active-org",
					Value = organizationId.ToString(),
					Url = frontendUrl.ToString(),
				},
			]);
		}

		await page.GotoAsync(frontendUrl.ToString());

		try
		{
			await page.GetByRole(AriaRole.Button, new() { Name = "User menu" })
				.WaitForAsync(new() { Timeout = 10_000 });
		}
		catch (TimeoutException ex)
		{
			throw new InvalidOperationException(
				"FastSignInAsync did not authenticate the SPA - oidc-client-ts's "
				+ "storage key/shape may have drifted from what's hardcoded here. "
				+ "Confirm with AuthHelper.LoginAsync, then update FastSignInAsync "
				+ "(see User.toStorageString in oidc-client-ts's source).", ex);
		}

		return pinnedOrganizationId;
	}

	internal static JsonElement DecodeJwtPayload(string jwt)
	{
		var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
		payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
		var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	public static async Task GoToOrgAppDashboardAsync(IPage page, Uri frontendUrl, Guid organizationId)
	{
		var origin = frontendUrl.GetLeftPart(UriPartial.Authority);
		await page.GotoAsync($"{origin}/app/{organizationId}/dashboard");
		await page.WaitForURLAsync(new Regex(@"/app/[^/]+/dashboard"), new() { Timeout = 15_000 });
	}

	public static async Task GoToOrgAppDashboardViaCtaAsync(IPage page, Uri frontendUrl)
	{
		await page.WaitForURLAsync($"{frontendUrl.GetLeftPart(UriPartial.Authority)}/", new() { Timeout = 15_000 });
		var cta = page.GetByRole(AriaRole.Link, new() { Name = "Go to dashboard" });
		await cta.First.WaitForAsync(new() { Timeout = 45_000 });
		await cta.First.ClickAsync();

		await page.WaitForURLAsync(new Regex(@"/app/[^/]+/dashboard"), new() { Timeout = 15_000 });
	}
}
