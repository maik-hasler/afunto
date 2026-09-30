using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects;
using TUnit.Core.Interfaces;

namespace VisualTests;

public class AspireFixture : IAsyncInitializer, IAsyncDisposable
{
	private const string Realm = "afunto";

	public const string BootTimingFileName = "aspire-boot-seconds.txt";

	private const string FrontendTestClientId = "frontend-test";

	private const string BackendClientId = "backend";
	private const string BackendClientSecret = "backend-secret";
	private const string OrganisatorRole = "organisator";

	private DistributedApplication _app = null!;
	private string _connectionString = null!;
	private HttpClient _keycloakClient = null!;

	private Guid _veraUserId;

	private Dictionary<Guid, Guid> _pinnedOrganizerOrgByUserId = null!;

	public async Task InitializeAsync()
	{
		var bootStartedAt = Stopwatch.GetTimestamp();

		var appHost = await DistributedApplicationTestingBuilder.CreateAsync<AppHost>(["--environment", "Testing"]);

		_app = await appHost.BuildAsync();
		await _app.StartAsync();

		var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

		foreach (var name in new[] { "keycloak", "backend", "frontend" })
		{
			await notifications
				.WaitForResourceAsync(name, KnownResourceStates.Running)
				.WaitAsync(TimeSpan.FromMinutes(5));
		}

		await WaitForRealmReadyAsync();
		await WarmKeycloakLoginPageAsync();
		await WaitForBackendReadyAsync();

		_connectionString = await _app.GetConnectionStringAsync("afunto")
			?? throw new InvalidOperationException("Connection string 'afunto' not found.");

		_keycloakClient = _app.CreateHttpClient("keycloak");

		await CaptureBaselineSnapshotAsync();

		var bootSeconds = Stopwatch.GetElapsedTime(bootStartedAt).TotalSeconds;
		Console.WriteLine($"[aspire-fixture] stack ready in {bootSeconds:F1}s");
		try
		{
			await File.WriteAllTextAsync(
				Path.Combine(AppContext.BaseDirectory, BootTimingFileName),
				bootSeconds.ToString("F1", CultureInfo.InvariantCulture));
		}
		catch (IOException)
		{
			// A timing breadcrumb is never worth failing a whole shard's boot over.
		}
	}

	public async Task ResetAsync()
	{
		await ResetVeraOrganizationMembershipAsync();
		await ResetVeraOrganisatorRoleAsync();
	}

	public Guid? GetPinnedOrganizerOrganizationId(Guid userId) =>
		_pinnedOrganizerOrgByUserId.TryGetValue(userId, out var organizationId) ? organizationId : null;

	public async Task<Guid?> GetCurrentFirstOrganizerOrganizationIdAsync(Guid userId)
	{
		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();
		var current = await ReadPinnedOrganizerOrgsAsync(conn);
		return current.TryGetValue(userId, out var organizationId) ? organizationId : null;
	}

	public async Task AddPlainMemberDirectlyAsync(
		Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
	{
		var adminToken = await GetAdminTokenAsync(cancellationToken);

		using var request = new HttpRequestMessage(
			HttpMethod.Post, $"/admin/realms/{Realm}/organizations/{organizationId}/members")
		{
			Content = JsonContent.Create(userId.ToString()),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await _keycloakClient.SendAsync(request, cancellationToken);
		await EnsureSuccessAsync(response);

		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync(cancellationToken);
		await using var cmd = new NpgsqlCommand(
			"""
			INSERT INTO organization_membership (id, organization_id, user_id, role, created_on)
			VALUES (@id, @organizationId, @userId, @role, now())
			ON CONFLICT (organization_id, user_id) DO NOTHING
			""", conn);
		cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
		cmd.Parameters.AddWithValue("organizationId", organizationId);
		cmd.Parameters.AddWithValue("userId", userId);
		cmd.Parameters.AddWithValue("role", "Member");
		await cmd.ExecuteNonQueryAsync(cancellationToken);
	}

	public static Guid OlafId { get; } = new("00000000-0000-0000-0000-000000000001");

	private async Task CaptureBaselineSnapshotAsync()
	{
		var vera = await SignInAsync("vera", "vera123");
		_veraUserId = vera.UserId;

		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();

		_pinnedOrganizerOrgByUserId = await ReadPinnedOrganizerOrgsAsync(conn);

		if (!_pinnedOrganizerOrgByUserId.ContainsKey(OlafId))
			throw new InvalidOperationException(
				"Seed data has no Organizer-role organization membership for olaf "
				+ $"(user id {OlafId}). ApplicationDbContextInitializer.SeedAsync likely "
				+ "failed partway through and Program.cs's Development branch logged "
				+ "rather than rethrowing - check the backend resource's startup logs "
				+ "for \"An exception occurred while seeding the database\".");
	}

	private static async Task<Dictionary<Guid, Guid>> ReadPinnedOrganizerOrgsAsync(NpgsqlConnection conn)
	{
		var pinned = new Dictionary<Guid, Guid>();
		await using var cmd = new NpgsqlCommand(
			"""
			SELECT m.user_id, o.id
			FROM organization_membership AS m
			JOIN organization AS o ON o.id = m.organization_id
			WHERE m.role = 'Organizer' AND NOT o.is_deleted
			ORDER BY m.user_id, o.name
			""", conn);
		await using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var userId = reader.GetGuid(0);
			if (!pinned.ContainsKey(userId))
				pinned[userId] = reader.GetGuid(1);
		}
		return pinned;
	}

	private async Task ResetVeraOrganizationMembershipAsync()
	{
		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();
		await using var cmd = new NpgsqlCommand(
			"""
			DELETE FROM organization_membership
			WHERE user_id = @userId
			""", conn);
		cmd.Parameters.AddWithValue("userId", _veraUserId);
		await cmd.ExecuteNonQueryAsync();
	}

	private async Task ResetVeraOrganisatorRoleAsync()
	{
		var adminToken = await GetAdminTokenAsync();

		using var roleRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/roles/{OrganisatorRole}");
		roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var roleResponse = await _keycloakClient.SendAsync(roleRequest);
		await EnsureSuccessAsync(roleResponse);

		var organisatorRole = await roleResponse.Content.ReadFromJsonAsync<KeycloakRole>()
			?? throw new InvalidOperationException("Keycloak role 'organisator' not found.");

		using var deleteRequest = new HttpRequestMessage(
			HttpMethod.Delete, $"/admin/realms/{Realm}/users/{_veraUserId}/role-mappings/realm")
		{
			Content = JsonContent.Create(new[] { new { id = organisatorRole.Id, name = organisatorRole.Name } }),
		};
		deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var deleteResponse = await _keycloakClient.SendAsync(deleteRequest);
		await EnsureSuccessAsync(deleteResponse);
	}

	private async Task<string> GetAdminTokenAsync(CancellationToken cancellationToken = default)
	{
		var response = await PostTokenRequestWithRetryAsync(
			_keycloakClient,
			$"/realms/{Realm}/protocol/openid-connect/token",
			() => new FormUrlEncodedContent([
				new KeyValuePair<string, string>("grant_type", "client_credentials"),
				new KeyValuePair<string, string>("client_id", BackendClientId),
				new KeyValuePair<string, string>("client_secret", BackendClientSecret),
			]),
			cancellationToken);

		await EnsureSuccessAsync(response);

		var token = await response.Content.ReadFromJsonAsync<AdminTokenResponse>(cancellationToken: cancellationToken)
			?? throw new InvalidOperationException("Keycloak returned no admin token.");

		return token.AccessToken;
	}

	private static Task<HttpResponseMessage> PostTokenRequestWithRetryAsync(
		HttpClient client, string requestUri, Func<FormUrlEncodedContent> contentFactory,
		CancellationToken cancellationToken = default)
		=> AuthHelper.PostTokenRequestWithRetryAsync(client, requestUri, contentFactory, cancellationToken);

	private static async Task EnsureSuccessAsync(HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode) return;

		var body = await response.Content.ReadAsStringAsync();
		throw new HttpRequestException(
			$"Keycloak {(int)response.StatusCode} for {response.RequestMessage?.Method} "
			+ $"{response.RequestMessage?.RequestUri}: {body}");
	}

	public async Task<KeycloakSession> SignInAsync(string username, string password)
	{
		using var client = _app.CreateHttpClient("keycloak");

		var response = await PostTokenRequestWithRetryAsync(
			client,
			$"/realms/{Realm}/protocol/openid-connect/token",
			() => new FormUrlEncodedContent([
				new KeyValuePair<string, string>("grant_type", "password"),
				new KeyValuePair<string, string>("client_id", FrontendTestClientId),
				new KeyValuePair<string, string>("username", username),
				new KeyValuePair<string, string>("password", password),
				new KeyValuePair<string, string>("scope", "openid"),
			]));
		await EnsureSuccessAsync(response);

		var token = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>()
			?? throw new InvalidOperationException("Keycloak returned no token.");

		var authority = $"{GetEndpoint("keycloak").ToString().TrimEnd('/')}/realms/{Realm}";

		var userId = Guid.Parse(
			AuthHelper.DecodeJwtPayload(token.IdToken).GetProperty("sub").GetString()!);

		return new KeycloakSession(
			token.AccessToken, token.IdToken, token.RefreshToken, token.ExpiresIn, token.TokenType, authority, userId);
	}

	public async Task<Guid> CreateThrowawayUserAsync(
		string username, string password, bool emailVerified, string[] requiredActions,
		IReadOnlyDictionary<string, string[]>? attributes = null,
		CancellationToken cancellationToken = default)
	{
		var adminToken = await GetAdminTokenAsync(cancellationToken);

		using var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/realms/{Realm}/users")
		{
			Content = JsonContent.Create(new
			{
				username,
				email = $"{username}@example.invalid",
				emailVerified,
				enabled = true,
				requiredActions,
				attributes,
				credentials = new[] { new { type = "password", value = password, temporary = false } },
			}),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await _keycloakClient.SendAsync(request, cancellationToken);
		await EnsureSuccessAsync(response);

		var location = response.Headers.Location?.ToString()
			?? throw new InvalidOperationException("Keycloak returned no Location header for the created user.");
		return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
	}

	public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var adminToken = await GetAdminTokenAsync(cancellationToken);

		using var request = new HttpRequestMessage(
			HttpMethod.Delete, $"/admin/realms/{Realm}/users/{userId}");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await _keycloakClient.SendAsync(request, cancellationToken);

		if (response.StatusCode == HttpStatusCode.NotFound)
			return;
		await EnsureSuccessAsync(response);
	}

	public async Task DeleteOpportunityRowDirectlyAsync(Guid opportunityId)
	{
		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();
		await using var cmd = new NpgsqlCommand(
			"DELETE FROM volunteer_opportunity WHERE id = @id", conn);
		cmd.Parameters.AddWithValue("id", opportunityId);
		await cmd.ExecuteNonQueryAsync();
	}

	private async Task WaitForBackendReadyAsync()
	{
		var backendEndpoint = _app.GetEndpoint("backend", "http");
		using var client = new HttpClient
		{
			BaseAddress = backendEndpoint,
			Timeout = TimeSpan.FromSeconds(5)
		};
		var deadline = DateTime.UtcNow.AddSeconds(120);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				var response = await client.GetAsync("/health");
				if (response.IsSuccessStatusCode)
					return;
			}
			catch (Exception)
			{
			}
			await Task.Delay(1000);
		}

		throw new TimeoutException("Backend did not become healthy in time.");
	}

	private async Task WarmKeycloakLoginPageAsync()
	{
		try
		{
			using var client = _app.CreateHttpClient("keycloak");

			using var response = await client.GetAsync(
				$"/realms/{Realm}/protocol/openid-connect/auth"
				+ "?client_id=frontend&response_type=code&scope=openid"
				+ "&redirect_uri=http%3A%2F%2Flocalhost%3A1%2Fcallback");

			response.EnsureSuccessStatusCode();
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			Console.WriteLine($"[aspire-fixture] login-page warm-up skipped: {ex.Message}");
		}
	}

	private async Task WaitForRealmReadyAsync()
	{
		using var client = _app.CreateHttpClient("keycloak");
		var deadline = DateTime.UtcNow.AddSeconds(300);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				var response = await client.GetAsync(
					$"/realms/{Realm}/.well-known/openid-configuration");
				if (response.IsSuccessStatusCode)
					return;
			}
			catch (HttpRequestException)
			{
			}
			await Task.Delay(1000);
		}

		throw new TimeoutException($"Keycloak realm '{Realm}' did not become ready in time.");
	}

	public Task WaitForResourceAsync(string resourceName) =>
		_app.Services
			.GetRequiredService<ResourceNotificationService>()
			.WaitForResourceAsync(resourceName, KnownResourceStates.Running)
			.WaitAsync(TimeSpan.FromSeconds(60));

	public Uri GetEndpoint(string resource, string endpointName = "http") =>
		_app.GetEndpoint(resource, endpointName);

	public async ValueTask DisposeAsync()
	{
		await _app.DisposeAsync();
		GC.SuppressFinalize(this);
	}

	private sealed record KeycloakTokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken,
		[property: JsonPropertyName("id_token")] string IdToken,
		[property: JsonPropertyName("refresh_token")] string? RefreshToken,
		[property: JsonPropertyName("expires_in")] int ExpiresIn,
		[property: JsonPropertyName("token_type")] string TokenType);

	private sealed record AdminTokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken);

	private sealed record KeycloakRole(string Id, string Name);
}

public sealed record KeycloakSession(
	string AccessToken,
	string IdToken,
	string? RefreshToken,
	int ExpiresIn,
	string TokenType,
	string Authority,
	Guid UserId);
