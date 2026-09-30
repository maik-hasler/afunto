using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class AcceptInvitationTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldReturn403_WhenUsingTheTokenFromBeforeAcceptingAnOrganizerInvitation(
		CancellationToken cancellationToken)
	{
		// Keycloak bakes role claims into the access token at issue time, so
		// the token the invitee used to accept still doesn't hold the
		// organisator role the acceptance itself just granted (afunto#2206).
		var (_, inviterUsername, inviterPassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var (inviteeId, inviteeUsername, inviteePassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);

		var inviterClient = await fixture.CreateAuthenticatedClientAsync(inviterUsername, inviterPassword);
		var org = await inviterClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Invitation Org {Guid.NewGuid()}" }, cancellationToken);

		inviterClient = await fixture.CreateAuthenticatedClientAsync(inviterUsername, inviterPassword);
		var invitation = await inviterClient.CreateInvitationAsync(
			org.Id.Value,
			new CreateInvitationRequest { InviteeId = inviteeId, Role = "Organizer" },
			cancellationToken);

		var inviteeClient = await fixture.CreateAuthenticatedClientAsync(inviteeUsername, inviteePassword);
		await inviteeClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);

		var act = () => inviteeClient.CreateVolunteerOpportunityAsync(
			BuildOpportunityRequest(org.Id.Value), cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldSucceed_WhenUsingATokenRefreshedAfterAcceptingAnOrganizerInvitation(
		CancellationToken cancellationToken)
	{
		// Refreshing the token - what the frontend's auth.signinSilent() does
		// right after accepting - re-issues it with the organisator role
		// Keycloak just granted, so the invitee's very next request carries
		// it (afunto#2206).
		var (_, inviterUsername, inviterPassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var (inviteeId, inviteeUsername, inviteePassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);

		var inviterClient = await fixture.CreateAuthenticatedClientAsync(inviterUsername, inviterPassword);
		var org = await inviterClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Invitation Org {Guid.NewGuid()}" }, cancellationToken);

		inviterClient = await fixture.CreateAuthenticatedClientAsync(inviterUsername, inviterPassword);
		var invitation = await inviterClient.CreateInvitationAsync(
			org.Id.Value,
			new CreateInvitationRequest { InviteeId = inviteeId, Role = "Organizer" },
			cancellationToken);

		var inviteeClient = await fixture.CreateAuthenticatedClientAsync(inviteeUsername, inviteePassword);
		await inviteeClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);

		inviteeClient = await fixture.CreateAuthenticatedClientAsync(inviteeUsername, inviteePassword);

		var opportunity = await inviteeClient.CreateVolunteerOpportunityAsync(
			BuildOpportunityRequest(org.Id.Value), cancellationToken);

		opportunity.Should().NotBeNull();
	}

	private static CreateVolunteerOpportunityRequest BuildOpportunityRequest(Guid organizationId) =>
		new()
		{
			TitleDe = "Test Opportunity",
			DescriptionDe = "Integration test opportunity",
			OrganizationId = organizationId,
			Street = "Test Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "Recurring",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		};
}
