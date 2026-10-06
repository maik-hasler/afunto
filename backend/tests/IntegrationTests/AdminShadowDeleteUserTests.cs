using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class AdminShadowDeleteUserTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetUserProfile_ShouldNotFail_WhenCallerWasShadowDeleted(
		CancellationToken cancellationToken)
	{
		var (userId, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var volunteerClient = await fixture.CreateAuthenticatedClientAsync(username, password);

		await volunteerClient.GetUserProfileAsync(cancellationToken);

		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		await adminClient.AdminShadowDeleteUserAsync(userId, cancellationToken);

		var profile = await volunteerClient.GetUserProfileAsync(cancellationToken);

		profile.Id.Should().Be(userId);
		profile.Username.Should().Be(username);
	}

	[Test]
	public async Task UpdateVolunteerOpportunity_ShouldNotFail_WhenSignedUpVolunteerWasShadowDeleted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var (volunteerUserId, volunteerUsername, volunteerPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var volunteerClient = await fixture.CreateAuthenticatedClientAsync(volunteerUsername, volunteerPassword);

		await volunteerClient.GetUserProfileAsync(cancellationToken);
		await volunteerClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		await adminClient.AdminShadowDeleteUserAsync(volunteerUserId, cancellationToken);

		var act = () => olafClient.UpdateVolunteerOpportunityAsync(
			opportunity.Id,
			new UpdateVolunteerOpportunityRequest
			{
				TitleDe = "Test Opportunity",
				DescriptionDe = "Integration test opportunity",
				IsRemote = false,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Hamburg",
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		await act.Should().NotThrowAsync();
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"ShadowDeleteTestOrg_{Guid.NewGuid()}";
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
