using Application.Common.Exceptions;
using AwesomeAssertions;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetOrganizationEngagementsTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturnEngagementsAcrossAllOpportunities_InTheOrganization(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunityA = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);
		var opportunityB = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		await veraClient.CreateEngagementAsync(
			opportunityA.Id, new CreateEngagementRequest { Message = "Helping with A" }, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunityB.Id, new CreateEngagementRequest { Message = "Helping with B" }, cancellationToken);

		var result = await olafClient.GetOrganizationEngagementsAsync(
			orgId, 1, 10, status: null, search: null, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(2);
		result.Items.Should().Contain(e => e.OpportunityId == opportunityA.Id);
		result.Items.Should().Contain(e => e.OpportunityId == opportunityB.Id);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldFilterByStatus(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var pending = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "Still pending" }, cancellationToken);

		var toConfirm = await olafClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "About to be confirmed" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(toConfirm.Id, cancellationToken);

		var pendingOnly = await olafClient.GetOrganizationEngagementsAsync(
			orgId, 1, 10, status: "Pending", search: null, cancellationToken: cancellationToken);

		pendingOnly.TotalItems.Should().Be(1);
		pendingOnly.Items.Should().ContainSingle(e => e.Id == pending.Id);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldNotReturnAnotherOrganizationsEngagements(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity1 = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunity1.Id, new CreateEngagementRequest { Message = "Helping org1" }, cancellationToken);

		var org2Id = await CreateOrganizationAsync(veraClient, cancellationToken);
		var veraOrganizerClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var opportunity2 = await CreateOpportunityAsync(veraOrganizerClient, org2Id, cancellationToken);
		await olafClient.CreateEngagementAsync(
			opportunity2.Id, new CreateEngagementRequest { Message = "Helping org2" }, cancellationToken);

		var org1Result = await olafClient.GetOrganizationEngagementsAsync(
			org1Id, 1, 10, status: null, search: null, cancellationToken: cancellationToken);

		org1Result.TotalItems.Should().Be(1);
		org1Result.Items.Should().ContainSingle(e => e.OpportunityId == opportunity1.Id);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturn403_WhenRequestingUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var act = () => veraClient.GetOrganizationEngagementsAsync(
			orgId, 1, 10, status: null, search: null, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturn404_WhenOrganizationNotFound(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var act = () => olafClient.GetOrganizationEngagementsAsync(
			Guid.NewGuid(), 1, 10, status: null, search: null, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturn400_ForInvalidPageNumber(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.GetOrganizationEngagementsAsync(
			orgId, 0, 10, status: null, search: null, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturn400_ForInvalidPageSize(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.GetOrganizationEngagementsAsync(
			orgId, 1, 101, status: null, search: null, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldReturn400_ForInvalidStatus(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.GetOrganizationEngagementsAsync(
			orgId, 1, 10, status: "NotAStatus", search: null, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationEngagements_ShouldPaginate_AcrossMultipleOpportunities(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunityA = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);
		var opportunityB = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		await veraClient.CreateEngagementAsync(
			opportunityA.Id, new CreateEngagementRequest { Message = "First" }, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunityB.Id, new CreateEngagementRequest { Message = "Second" }, cancellationToken);

		await olafClient.CreateEngagementAsync(
			opportunityA.Id, new CreateEngagementRequest { Message = "Third" }, cancellationToken);

		var firstPage = await olafClient.GetOrganizationEngagementsAsync(
			orgId, 1, 2, status: null, search: null, cancellationToken: cancellationToken);
		var secondPage = await olafClient.GetOrganizationEngagementsAsync(
			orgId, 2, 2, status: null, search: null, cancellationToken: cancellationToken);

		firstPage.TotalItems.Should().Be(3);
		firstPage.PageCount.Should().Be(2);
		firstPage.Items.Should().HaveCount(2);
		secondPage.Items.Should().HaveCount(1);

		var allIds = firstPage.Items.Concat(secondPage.Items).Select(e => e.Id).ToList();
		allIds.Should().OnlyHaveUniqueItems();
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"OrgEngagementsTestOrg_{Guid.NewGuid()}";
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return org.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Test Opportunity",
				DescriptionDe = "Integration test opportunity",
				OrganizationId = orgId,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);
	}
}
