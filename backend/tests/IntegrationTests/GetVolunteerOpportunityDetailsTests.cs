using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetVolunteerOpportunityDetailsTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldReturn404_WhenOpportunityIsDraftAndRequesterIsAnonymous(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateDraftOpportunityAsync(olafClient, orgId, cancellationToken);

		var anonymousClient = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => anonymousClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldReturn404_WhenOpportunityIsDraftAndRequesterIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateDraftOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldReturnDetails_WhenOpportunityIsDraftAndRequesterIsOrganizer(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateDraftOpportunityAsync(olafClient, orgId, cancellationToken);

		var details = await olafClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		details.Should().NotBeNull();
		details.Status.Should().Be("Draft");
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldReturnDetails_WhenOpportunityIsPublishedAndRequesterIsAnonymous(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateDraftOpportunityAsync(olafClient, orgId, cancellationToken);
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var anonymousClient = new AfuntoApi(fixture.CreateHttpClient());

		var details = await anonymousClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		details.Should().NotBeNull();
		details.Status.Should().Be("Published");
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldExcludeViewerFromParticipantCount_WhenViewerHasSignedUp(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateDraftOpportunityAsync(olafClient, orgId, cancellationToken);
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var detailsForVera = await veraClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);
		var detailsForOlaf = await olafClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		detailsForVera.CurrentParticipantCount.Should().Be(0);
		detailsForOlaf.CurrentParticipantCount.Should().Be(1);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"DetailsTestOrg_{Guid.NewGuid()}";
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return org.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateDraftOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken) =>
		await client.CreateVolunteerOpportunityAsync(
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
				IsDraft = true,
			},
			cancellationToken);
}
