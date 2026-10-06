using System.Net.Http.Headers;
using AwesomeAssertions;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class OrganizationOpportunitiesTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturnEmptyPagedList_WhenNoneExistForStatus(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		var result = await client.GetOrganizationOpportunitiesAsync(orgId, "Published", 1, 10, cancellationToken);

		result.TotalItems.Should().Be(0);
		result.Items.Should().BeEmpty();
		result.CurrentPage.Should().Be(1);
		result.PageCount.Should().Be(0);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturnOnlyDrafts_WhenStatusIsDraft(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		await CreateDraftOpportunityAsync(client, orgId, "Draft 1", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Published 1", cancellationToken);

		var result = await client.GetOrganizationOpportunitiesAsync(orgId, "Draft", 1, 10, cancellationToken);

		result.TotalItems.Should().Be(1);
		result.Items.Single().TitleDe.Should().Be("Draft 1");
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturnOnlyPublished_WhenStatusIsPublished(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		await CreateDraftOpportunityAsync(client, orgId, "Draft 1", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Published 1", cancellationToken);

		var result = await client.GetOrganizationOpportunitiesAsync(orgId, "Published", 1, 10, cancellationToken);

		result.TotalItems.Should().Be(1);
		result.Items.Single().TitleDe.Should().Be("Published 1");
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturnCorrectPageSize_WhenPaginationIsApplied(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 1", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 2", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 3", cancellationToken);

		var result = await client.GetOrganizationOpportunitiesAsync(orgId, "Published", 1, 2, cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(2);
		result.PageCount.Should().Be(2);
		result.CurrentPage.Should().Be(1);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturnRemainingItems_WhenRequestingLastPage(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 1", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 2", cancellationToken);
		await CreatePublishedOpportunityAsync(client, orgId, "Opportunity 3", cancellationToken);

		var result = await client.GetOrganizationOpportunitiesAsync(orgId, "Published", 2, 2, cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(1);
		result.CurrentPage.Should().Be(2);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturn400_WhenStatusIsInvalid(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		var act = () => client.GetOrganizationOpportunitiesAsync(orgId, "NotAStatus", 1, 10, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturn400_WhenPageNumberIsLessThanOne(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		var act = () => client.GetOrganizationOpportunitiesAsync(orgId, "Published", 0, 10, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturn400_WhenPageSizeIsOutOfRange(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(client, cancellationToken);

		var act = () => client.GetOrganizationOpportunitiesAsync(orgId, "Published", 1, 101, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var anonClient = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => anonClient.GetOrganizationOpportunitiesAsync(Guid.NewGuid(), "Published", 1, 10, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldReturn403_WhenOrganisatorAccessesOtherOrgsOpportunities(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);

		var veraToken = await fixture.GetAccessTokenAsync("vera", "vera123");
		var veraHttpClient = fixture.CreateHttpClient();
		veraHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", veraToken);
		var veraClient = new AfuntoApi(veraHttpClient);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.GetOrganizationOpportunitiesAsync(org1Id, "Published", 1, 10, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task GetOrganizationOpportunities_ShouldSucceed_WhenRequestingUserIsAPlainMember(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		await CreatePublishedOpportunityAsync(olafClient, orgId, "Published 1", cancellationToken);

		var veraToken = await fixture.GetAccessTokenAsync("vera", "vera123");
		var veraHttpClient = fixture.CreateHttpClient();
		veraHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", veraToken);
		var veraClient = new AfuntoApi(veraHttpClient);
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);

		await fixture.AddPlainMemberDirectlyAsync(orgId, vera.Id, cancellationToken);

		var result = await veraClient.GetOrganizationOpportunitiesAsync(orgId, "Published", 1, 10, cancellationToken);

		result.TotalItems.Should().Be(1);
		result.Items.Single().TitleDe.Should().Be("Published 1");
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"OrgOpportunitiesTestOrg_{Guid.NewGuid()}";
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return organization.Id.Value;
	}

	private static Task<CreateVolunteerOpportunityResponse> CreateDraftOpportunityAsync(
		AfuntoApi client, Guid orgId, string title, CancellationToken cancellationToken) =>
		client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Integration test opportunity",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

	private static async Task<CreateVolunteerOpportunityResponse> CreatePublishedOpportunityAsync(
		AfuntoApi client, Guid orgId, string title, CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Integration test opportunity",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
		}, cancellationToken);

		return opportunity;
	}
}
