using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Common.Exceptions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects;
using Respawn;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

public class IntegrationTestFixture
	: IAsyncInitializer,
	IAsyncDisposable
{
	private const string Realm = "afunto";
	private const string FrontendClientId = "frontend-test";
	private const string BackendClientId = "backend";
	private const string BackendClientSecret = "backend-secret";
	private const string OrganisatorRole = "organisator";
	private const string DefaultUserRole = "user";
	private const string BaselineOrganisator = "olaf";

	// AppHost runs the other limits at 10000, effectively off; this fixture is the only
	// caller that dials one back down, so that RateLimitingTests has a real limiter to
	// trip. That dialled-down bucket is shared, though: every client this fixture hands
	// out reaches the backend over loopback, and the anonymous Read bucket is partitioned
	// by the real connection IP (see RateLimitingExtensions.GetClientIp), so all 600-odd
	// tests draw on a single one. At the production value of 60/min the suite's own ordinary
	// anonymous traffic already rode that ceiling, and tests began failing on 429s raised
	// by their neighbours' requests rather than their own. 300 leaves ordinary traffic
	// room while staying cheap to exhaust deliberately; the burst tests scale their
	// request counts off these values rather than hardcoding one. The tile budget has to
	// clear the Read limit, because MapTileRateLimitingTests shows tiles outlasting the
	// content bucket by out-requesting it - and AppHost only forwards this because of the
	// RateLimiting:MapTiles:PermitLimit passthrough added alongside these values.
	public const int AnonymousReadPermitLimit = 300;
	public const int MapTilesPermitLimit = 2000;

	private DistributedApplication _app = null!;
	private Respawner _respawner = null!;
	private string _connectionString = null!;
	private HttpClient _keycloakClient = null!;

	public async Task InitializeAsync()
	{
		var appHost = await DistributedApplicationTestingBuilder
			.CreateAsync<AppHost>([
				"--environment", "Testing",
				$"--RateLimiting:Read:AnonymousPermitLimit={AnonymousReadPermitLimit}",
				$"--RateLimiting:MapTiles:PermitLimit={MapTilesPermitLimit}",
			]);

		_app = await appHost.BuildAsync();
		await _app.StartAsync();

		var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

		await notifications
			.WaitForResourceAsync("keycloak", KnownResourceStates.Running)
			.WaitAsync(TimeSpan.FromMinutes(5));

		await notifications
			.WaitForResourceAsync("backend", KnownResourceStates.Running)
			.WaitAsync(TimeSpan.FromMinutes(5));

		_keycloakClient = _app.CreateHttpClient("keycloak");

		var backendClient = _app.CreateHttpClient("backend", "http");
		await WaitForBackendReadyAsync(backendClient);

		await WaitForRealmReadyAsync();

		_connectionString = await _app.GetConnectionStringAsync("afunto")
			?? throw new InvalidOperationException("Connection string 'afunto' not found.");

		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();

		_respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
		{
			DbAdapter = DbAdapter.Postgres,
			SchemasToInclude = ["public"],
			TablesToIgnore = ["__EFMigrationsHistory"],
		});

		await _respawner.ResetAsync(conn);
		await ResetKeycloakOrganizationsAsync();
		await ResetKeycloakOrganisatorRolesAsync();
	}

	public async ValueTask DisposeAsync() =>
		await _app.DisposeAsync();

	public HttpClient CreateHttpClient() =>
		_app.CreateHttpClient("backend", "http");

	public string GetStorageEndpoint()
	{
		using var client = _app.CreateHttpClient("storage", "api");
		return client.BaseAddress!.ToString();
	}

	public HttpClient CreateMailpitClient() => _app.CreateHttpClient("mailpit", "webui");

	public async Task<HttpClient> CreateKeycloakAdminClientAsync()
	{
		var client = _app.CreateHttpClient("keycloak");
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());
		return client;
	}

	public async Task<string> GetAccessTokenAsync(string username, string password)
	{
		var content = new FormUrlEncodedContent([
			new KeyValuePair<string, string>("grant_type", "password"),
			new KeyValuePair<string, string>("client_id", FrontendClientId),
			new KeyValuePair<string, string>("username", username),
			new KeyValuePair<string, string>("password", password),
			new KeyValuePair<string, string>("scope", "openid"),
		]);

		var response = await _keycloakClient.PostAsync(
			$"/realms/{Realm}/protocol/openid-connect/token", content);

		await EnsureSuccessAsync(response);

		var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
			?? throw new InvalidOperationException("Keycloak returned no token.");

		return token.AccessToken;
	}

	// The one way these tests get an authenticated client: token, HttpClient, Bearer
	// header, typed client. It belongs on the fixture because both halves of it already
	// do - the 39 private copies this replaces each called CreateHttpClient and
	// GetAccessTokenAsync, so the duplication was never more than a wrapper over two
	// methods that live here.
	//
	// Two arguments, and deliberately no "sign in as olaf" convenience overload: 242
	// call sites across 32 files already spell that pair out, so the seven copies that
	// baked it into the helper were the minority dialect, not the convention. Their
	// call sites now say which user they are, like every other one does.
	//
	// No CancellationToken parameter either. GetAccessTokenAsync above takes none, so
	// one here could only be accepted and dropped - exactly what six of the deleted
	// copies did, taking a token their body never passed on.
	public async Task<AfuntoApi> CreateAuthenticatedClientAsync(
		string username, string password)
	{
		var token = await GetAccessTokenAsync(username, password);
		var httpClient = CreateHttpClient();
		httpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
		return new AfuntoApi(httpClient);
	}

	public async Task ResetDatabaseAsync()
	{
		const int maxAttempts = 3;

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				await using var conn = new NpgsqlConnection(_connectionString);
				await conn.OpenAsync();
				await _respawner.ResetAsync(conn);
				return;
			}
			catch (NpgsqlException) when (attempt < maxAttempts)
			{
				await Task.Delay(TimeSpan.FromSeconds(attempt));
			}
		}
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

	public async Task<int> CountRowsWhereAsync(string table, string column, Guid value)
	{
		await using var conn = new NpgsqlConnection(_connectionString);
		await conn.OpenAsync();
		await using var cmd = new NpgsqlCommand(
			$"SELECT COUNT(*) FROM \"{table}\" WHERE {column} = @value", conn);
		cmd.Parameters.AddWithValue("value", value);
		var count = await cmd.ExecuteScalarAsync();
		return Convert.ToInt32(count);
	}

	public async Task<int> CountOutboxMessagesOfTypeAsync(string domainEventTypeFullName)
	{
		await using var context = CreateApplicationDbContext();
		return await context.Set<OutboxMessage>()
			.CountAsync(m => m.Type == domainEventTypeFullName);
	}

	public async Task<bool> WaitForOutboxMessageProcessedAsync(
		string domainEventTypeFullName, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow.Add(timeout);

		while (DateTime.UtcNow < deadline)
		{
			await using var context = CreateApplicationDbContext();
			var processed = await context.Set<OutboxMessage>()
				.AnyAsync(m => m.Type == domainEventTypeFullName && m.ProcessedOnUtc != null);

			if (processed) return true;

			await Task.Delay(500);
		}

		return false;
	}

	internal ApplicationDbContext CreateApplicationDbContext()
	{
		var options = new DbContextOptionsBuilder<ApplicationDbContext>()
			.UseNpgsql(_connectionString)
			.UseSnakeCaseNamingConvention()
			.Options;
		return new ApplicationDbContext(options);
	}

	public async Task ResetAsync()
	{
		await ResetDatabaseAsync();
		await ResetKeycloakOrganizationsAsync();
		await ResetKeycloakOrganisatorRolesAsync();
	}

	public async Task ResetKeycloakOrganizationsAsync()
	{
		var adminToken = await GetAdminTokenAsync();

		using var listRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/organizations?max=1000");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var listResponse = await _keycloakClient.SendAsync(listRequest);
		await EnsureSuccessAsync(listResponse);

		var orgs = await listResponse.Content.ReadFromJsonAsync<List<KeycloakOrganization>>()
			?? [];

		foreach (var org in orgs)
		{
			using var deleteRequest = new HttpRequestMessage(
				HttpMethod.Delete, $"/admin/realms/{Realm}/organizations/{org.Id}");
			deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

			var deleteResponse = await _keycloakClient.SendAsync(deleteRequest);
			await EnsureSuccessAsync(deleteResponse);
		}
	}

	public async Task ResetKeycloakOrganisatorRolesAsync()
	{
		var adminToken = await GetAdminTokenAsync();

		using var roleRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/roles/{OrganisatorRole}");
		roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var roleResponse = await _keycloakClient.SendAsync(roleRequest);
		await EnsureSuccessAsync(roleResponse);

		var organisatorRole = await roleResponse.Content.ReadFromJsonAsync<KeycloakRole>()
			?? throw new InvalidOperationException("Keycloak role 'organisator' not found.");

		using var usersRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/roles/{OrganisatorRole}/users?max=1000");
		usersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var usersResponse = await _keycloakClient.SendAsync(usersRequest);
		await EnsureSuccessAsync(usersResponse);

		var users = await usersResponse.Content.ReadFromJsonAsync<List<KeycloakUser>>() ?? [];
		var hasBaselineOrganisator = false;

		foreach (var user in users)
		{
			if (string.Equals(user.Username, BaselineOrganisator, StringComparison.OrdinalIgnoreCase))
			{
				hasBaselineOrganisator = true;
				continue;
			}

			using var deleteRequest = new HttpRequestMessage(
				HttpMethod.Delete, $"/admin/realms/{Realm}/users/{user.Id}/role-mappings/realm")
			{
				Content = JsonContent.Create(new[] { new { id = organisatorRole.Id, name = organisatorRole.Name } }),
			};
			deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

			var deleteResponse = await _keycloakClient.SendAsync(deleteRequest);
			await EnsureSuccessAsync(deleteResponse);
		}

		if (!hasBaselineOrganisator)
		{
			using var lookupRequest = new HttpRequestMessage(
				HttpMethod.Get, $"/admin/realms/{Realm}/users?username={BaselineOrganisator}&exact=true");
			lookupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

			var lookupResponse = await _keycloakClient.SendAsync(lookupRequest);
			await EnsureSuccessAsync(lookupResponse);

			var matches = await lookupResponse.Content.ReadFromJsonAsync<List<KeycloakUser>>() ?? [];
			var baselineUser = matches.SingleOrDefault(u => string.Equals(u.Username, BaselineOrganisator, StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException($"Keycloak user '{BaselineOrganisator}' not found.");

			using var assignRequest = new HttpRequestMessage(
				HttpMethod.Post, $"/admin/realms/{Realm}/users/{baselineUser.Id}/role-mappings/realm")
			{
				Content = JsonContent.Create(new[] { new { id = organisatorRole.Id, name = organisatorRole.Name } }),
			};
			assignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

			var assignResponse = await _keycloakClient.SendAsync(assignRequest);
			await EnsureSuccessAsync(assignResponse);
		}
	}

	public async Task<bool> UserHasOrganisatorRoleAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var adminToken = await GetAdminTokenAsync();

		using var usersRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/roles/{OrganisatorRole}/users?max=1000");
		usersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var usersResponse = await _keycloakClient.SendAsync(usersRequest, cancellationToken);
		await EnsureSuccessAsync(usersResponse);

		var users = await usersResponse.Content.ReadFromJsonAsync<List<KeycloakUser>>(cancellationToken) ?? [];

		return users.Any(u => Guid.Parse(u.Id) == userId);
	}

	public async Task AddPlainMemberDirectlyAsync(
		Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
	{
		var adminToken = await GetAdminTokenAsync();

		using var request = new HttpRequestMessage(
			HttpMethod.Post, $"/admin/realms/{Realm}/organizations/{organizationId}/members")
		{
			Content = JsonContent.Create(userId.ToString()),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var response = await _keycloakClient.SendAsync(request, cancellationToken);
		await EnsureSuccessAsync(response);

		await using var dbContext = CreateApplicationDbContext();
		var membership = Domain.Organizations.OrganizationMembership.Create(
			Domain.Organizations.OrganizationId.Create(organizationId).GetValueOrThrow(),
			Domain.Users.UserId.Create(userId).GetValueOrThrow(),
			Domain.Organizations.OrganizationMemberRole.Member);
		dbContext.Set<Domain.Organizations.OrganizationMembership>().Add(membership);
		await dbContext.SaveChangesAsync(cancellationToken);
	}

	public async Task<(Guid UserId, string Username, string Password)> CreateEphemeralUserAsync(
		CancellationToken cancellationToken = default)
	{
		var username = $"itest-{Guid.NewGuid():N}";
		const string password = "TestPass1";

		var adminToken = await GetAdminTokenAsync();

		using var createRequest = new HttpRequestMessage(
			HttpMethod.Post, $"/admin/realms/{Realm}/users")
		{
			Content = JsonContent.Create(new
			{
				username,
				enabled = true,
				emailVerified = true,
				email = $"{username}@example.com",
				credentials = new[]
				{
					new { type = "password", value = password, temporary = false },
				},
			}),
		};
		createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var createResponse = await _keycloakClient.SendAsync(createRequest, cancellationToken);
		await EnsureSuccessAsync(createResponse);

		var location = createResponse.Headers.Location
			?? throw new InvalidOperationException("Keycloak did not return a Location header for the new user.");
		var userId = location.Segments[^1];

		using var roleRequest = new HttpRequestMessage(
			HttpMethod.Get, $"/admin/realms/{Realm}/roles/{DefaultUserRole}");
		roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var roleResponse = await _keycloakClient.SendAsync(roleRequest, cancellationToken);
		await EnsureSuccessAsync(roleResponse);

		var userRole = await roleResponse.Content.ReadFromJsonAsync<KeycloakRole>(cancellationToken)
			?? throw new InvalidOperationException("Keycloak role 'user' not found.");

		using var assignRequest = new HttpRequestMessage(
			HttpMethod.Post, $"/admin/realms/{Realm}/users/{userId}/role-mappings/realm")
		{
			Content = JsonContent.Create(new[] { new { id = userRole.Id, name = userRole.Name } }),
		};
		assignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

		var assignResponse = await _keycloakClient.SendAsync(assignRequest, cancellationToken);
		await EnsureSuccessAsync(assignResponse);

		return (Guid.Parse(userId), username, password);
	}

	public Task WaitForResourceAsync(string resourceName) =>
		_app.Services
			.GetRequiredService<ResourceNotificationService>()
			.WaitForResourceAsync(resourceName, KnownResourceStates.Running)
			.WaitAsync(TimeSpan.FromSeconds(60));

	private async Task<string> GetAdminTokenAsync()
	{
		var content = new FormUrlEncodedContent([
			new KeyValuePair<string, string>("grant_type", "client_credentials"),
			new KeyValuePair<string, string>("client_id", BackendClientId),
			new KeyValuePair<string, string>("client_secret", BackendClientSecret),
		]);

		var response = await _keycloakClient.PostAsync(
			$"/realms/{Realm}/protocol/openid-connect/token", content);

		await EnsureSuccessAsync(response);

		var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
			?? throw new InvalidOperationException("Keycloak returned no admin token.");

		return token.AccessToken;
	}

	private static async Task WaitForBackendReadyAsync(HttpClient client)
	{
		var deadline = DateTime.UtcNow.AddSeconds(300);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				var response = await client.GetAsync("/alive", cts.Token);
				if (response.IsSuccessStatusCode)
					return;
			}
			catch (Exception) { }
			await Task.Delay(1000);
		}
		throw new TimeoutException("Backend did not become ready in time.");
	}

	private async Task WaitForRealmReadyAsync()
	{
		var deadline = DateTime.UtcNow.AddSeconds(300);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				var response = await _keycloakClient.GetAsync(
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

	private static async Task EnsureSuccessAsync(HttpResponseMessage response)
	{
		if (response.IsSuccessStatusCode) return;

		var body = await response.Content.ReadAsStringAsync();
		throw new HttpRequestException(
			$"Keycloak {(int)response.StatusCode} for {response.RequestMessage?.Method} " +
			$"{response.RequestMessage?.RequestUri}: {body}");
	}

	private sealed record TokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken);

	private sealed record KeycloakOrganization(string Id, string Name);

	private sealed record KeycloakRole(string Id, string Name);

	private sealed record KeycloakUser(string Id, string Username);
}
