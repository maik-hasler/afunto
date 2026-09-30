using System.Net.Http.Headers;
using AwesomeAssertions;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class OutputCachingTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	private static HttpClient WithForwardedFor(HttpClient client, string ip)
	{
		client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
		return client;
	}

	[Test]
	public async Task GetBadgeCatalog_ShouldBeServedFromOutputCache_OnASecondRequest(
		CancellationToken cancellationToken)
	{
		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.1");

		await httpClient.GetAsync("/v1/badges", cancellationToken);
		var second = await httpClient.GetAsync("/v1/badges", cancellationToken);

		second.Headers.TryGetValues("Age", out _).Should().BeTrue(
			"the badge catalog is a public, non-personalized endpoint and should be output-cached (#1391)");
	}

	[Test]
	public async Task GetSitemap_ShouldBeServedFromOutputCache_OnASecondRequest(
		CancellationToken cancellationToken)
	{
		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.2");

		await httpClient.GetAsync("/v1/sitemap.xml", cancellationToken);
		var second = await httpClient.GetAsync("/v1/sitemap.xml", cancellationToken);

		second.Headers.TryGetValues("Age", out _).Should().BeTrue(
			"the sitemap is a public, non-personalized endpoint and should be output-cached (afunto#1092)");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldBeServedFromOutputCache_OnASecondRequest(
		CancellationToken cancellationToken)
	{
		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.3");
		const string route = "/v1/volunteer-opportunities?pageNumber=1&pageSize=10";

		await httpClient.GetAsync(route, cancellationToken);
		var second = await httpClient.GetAsync(route, cancellationToken);

		second.Headers.TryGetValues("Age", out _).Should().BeTrue();
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReflectNewOpportunity_ImmediatelyAfterCreate(
		CancellationToken cancellationToken)
	{
		const string route = "/v1/volunteer-opportunities?pageNumber=1&pageSize=10";

		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.4");
		var before = await httpClient.GetAsync(route, cancellationToken);
		var beforeBody = await before.Content.ReadAsStringAsync(cancellationToken);
		beforeBody.Should().NotContain("Freshly published opportunity");

		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, "Cache Eviction Org", cancellationToken);

		await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Freshly published opportunity",
			DescriptionDe = "Proves a create evicts the output cache (#1543)",
			OrganizationId = orgId,
			IsRemote = true,
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
		}, cancellationToken);

		var after = await httpClient.GetAsync(route, cancellationToken);
		var afterBody = await after.Content.ReadAsStringAsync(cancellationToken);

		afterBody.Should().Contain("Freshly published opportunity",
			"creating a volunteer opportunity must evict the listing's output cache tag " +
			"instead of leaving the previous (stale) response cached for ShortPublicReadSeconds");
	}

	[Test]
	public async Task GetHealth_ShouldBeServedFromOutputCache_OnASecondRequest(
		CancellationToken cancellationToken)
	{
		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.5");

		await httpClient.GetAsync("/health", cancellationToken);
		var second = await httpClient.GetAsync("/health", cancellationToken);

		second.Headers.TryGetValues("Age", out _).Should().BeTrue();
	}

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldCacheEachOrganizationSeparately(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var firstOrgId = await CreateOrganizationAsync(authenticatedClient, "Org One", cancellationToken);
		var secondOrgId = await CreateOrganizationAsync(authenticatedClient, "Org Two", cancellationToken);

		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.6");

		await httpClient.GetAsync($"/v1/organizations/{firstOrgId}/profile", cancellationToken);
		var firstResponse = await httpClient.GetAsync($"/v1/organizations/{firstOrgId}/profile", cancellationToken);

		await httpClient.GetAsync($"/v1/organizations/{secondOrgId}/profile", cancellationToken);
		var secondResponse = await httpClient.GetAsync($"/v1/organizations/{secondOrgId}/profile", cancellationToken);

		firstResponse.Headers.TryGetValues("Age", out _).Should().BeTrue();
		secondResponse.Headers.TryGetValues("Age", out _).Should().BeTrue();

		var firstBody = await firstResponse.Content.ReadAsStringAsync(cancellationToken);
		var secondBody = await secondResponse.Content.ReadAsStringAsync(cancellationToken);

		firstBody.Should().Contain("Org One").And.NotContain("Org Two");
		secondBody.Should().Contain("Org Two").And.NotContain("Org One");
	}

	[Test]
	public async Task SearchCities_ShouldCacheEachLanguageSeparately(
		CancellationToken cancellationToken)
	{
		using var httpClient = WithForwardedFor(fixture.CreateHttpClient(), "10.0.2.7");
		const string route = "/v1/maps/cities?q=Berlin";

		httpClient.DefaultRequestHeaders.Add("X-Language", "de");
		await httpClient.GetAsync(route, cancellationToken);
		var germanSecondRequest = await httpClient.GetAsync(route, cancellationToken);

		httpClient.DefaultRequestHeaders.Remove("X-Language");
		httpClient.DefaultRequestHeaders.Add("X-Language", "en");
		var englishFirstRequest = await httpClient.GetAsync(route, cancellationToken);

		germanSecondRequest.Headers.TryGetValues("Age", out _).Should().BeTrue(
			"the second identical German-language request should be served from the output cache");
		englishFirstRequest.Headers.TryGetValues("Age", out _).Should().BeFalse(
			"a different X-Language must be a cache miss, not reuse the German response cached above");
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldNotBeOutputCached(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, "Org", cancellationToken);
		var token = await fixture.GetAccessTokenAsync("olaf", "olaf123");

		using var httpClient = fixture.CreateHttpClient();
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var route = $"/v1/organizations/{orgId}/opportunities?status=Published&pageNumber=1&pageSize=10";

		await httpClient.GetAsync(route, cancellationToken);
		var second = await httpClient.GetAsync(route, cancellationToken);

		second.Headers.TryGetValues("Age", out _).Should().BeFalse();
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, string name, CancellationToken cancellationToken)
	{
		var uniqueName = $"{name}_{Guid.NewGuid()}";
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return organization.Id.Value;
	}
}
