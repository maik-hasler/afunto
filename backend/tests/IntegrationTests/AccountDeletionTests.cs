using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class AccountDeletionTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task DeleteMyAccount_ShouldRemoveAccountAcrossAllSubsystems_AndAnonymizeButNotDeleteEngagementHistory(
		CancellationToken cancellationToken)
	{
		var (ephemeralUserId, ephemeralUsername, ephemeralPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);
		// The SPA loads the profile on every sign-in, which is what creates the local user row.
		await ephemeralClient.GetUserProfileAsync(cancellationToken);

		var (thirdPartyUserId, thirdPartyUsername, thirdPartyPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		// Reporting needs a local user row, which a first profile load creates.
		var thirdPartyClient = await fixture.CreateAuthenticatedClientAsync(thirdPartyUsername, thirdPartyPassword);
		await thirdPartyClient.GetUserProfileAsync(cancellationToken);

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Account Deletion Test Org" }, cancellationToken);
		var opportunity = await olafClient.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Account Deletion Test Opportunity",
				DescriptionDe = "Integration test opportunity for account deletion",
				OrganizationId = org.Id.Value,
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

		var engagement = await ephemeralClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var sharedOrg = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Shared Membership Test Org" }, cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			sharedOrg.Id.Value,
			new CreateInvitationRequest { InviteeId = ephemeralUserId, Role = "Organizer" },
			cancellationToken);
		await ephemeralClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);

		ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		await ephemeralClient.SaveDashboardLayoutAsync(
			sharedOrg.Id.Value,
			new SaveDashboardLayoutRequest
			{
				Widgets =
				[
					new DashboardWidgetPlacementRequest { WidgetKey = "ToDo", X = 1, Y = 1, Width = 1, Height = 1 },
				],
			},
			cancellationToken);

		await ephemeralClient.CreateInvitationAsync(
			sharedOrg.Id.Value,
			new CreateInvitationRequest { InviteeId = thirdPartyUserId, Role = "Member" },
			cancellationToken);

		await ephemeralClient.ReportUserAsync(
			thirdPartyUserId,
			new ReportUserRequest { Reason = "Spam" },
			cancellationToken);

		await ephemeralClient.DeleteMyAccountAsync(cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.Users.UserAccountDeletedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("UserAccountDeletedDomainEventHandler should have deleted the Keycloak user by now");

		var reLogin = () => fixture.GetAccessTokenAsync(ephemeralUsername, ephemeralPassword);
		await reLogin.Should().ThrowAsync<Exception>();

		(await fixture.CountRowsWhereAsync("notification", "recipient_id", ephemeralUserId))
			.Should().Be(0);

		(await fixture.CountRowsWhereAsync("user", "id", ephemeralUserId))
			.Should().Be(0);

		var engagements = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);
		engagements.Items.Should().ContainSingle(e => e.Id == engagement.Id && e.VolunteerId == null);

		(await fixture.CountRowsWhereAsync("user_streak", "user_id", ephemeralUserId))
			.Should().Be(0);
		(await fixture.CountRowsWhereAsync("achievement", "user_id", ephemeralUserId))
			.Should().Be(0);

		(await fixture.CountRowsWhereAsync("organization_membership", "user_id", ephemeralUserId))
			.Should().Be(0);
		(await fixture.CountRowsWhereAsync("organization_dashboard_layout", "user_id", ephemeralUserId))
			.Should().Be(0);

		(await fixture.CountRowsWhereAsync("organization_invitation", "invitee_id", ephemeralUserId))
			.Should().Be(0);
		(await fixture.CountRowsWhereAsync("organization_invitation", "invited_by_id", ephemeralUserId))
			.Should().Be(0);

		(await fixture.CountRowsWhereAsync("report", "reporter_id", ephemeralUserId))
			.Should().Be(0);
	}

	[Test]
	public async Task DeleteMyAccount_ShouldRemoveAccountAcrossAllSubsystems_WhenCallerWasPreviouslyShadowDeleted(
		CancellationToken cancellationToken)
	{
		var (ephemeralUserId, ephemeralUsername, ephemeralPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		await ephemeralClient.GetUserProfileAsync(cancellationToken);

		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		await adminClient.AdminShadowDeleteUserAsync(ephemeralUserId, cancellationToken);

		await ephemeralClient.DeleteMyAccountAsync(cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.Users.UserAccountDeletedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue(
			"MarkAccountDeleted must still run for a shadow-deleted user, so UserAccountDeletedDomainEventHandler deletes the Keycloak user");

		var reLogin = () => fixture.GetAccessTokenAsync(ephemeralUsername, ephemeralPassword);
		await reLogin.Should().ThrowAsync<Exception>();

		(await fixture.CountRowsWhereAsync("user", "id", ephemeralUserId))
			.Should().Be(0, "the local row must be hard-deleted, not left behind hidden under IsDeleted=true");
	}

	[Test]
	public async Task DeleteMyAccount_ShouldReturn404_WhenLocalUserRowDoesNotExistAtAll(
		CancellationToken cancellationToken)
	{
		var (_, ephemeralUsername, ephemeralPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		var deleteAccount = () => ephemeralClient.DeleteMyAccountAsync(cancellationToken);

		var ex = await deleteAccount.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task DeleteMyAccount_ShouldBeBlocked_WhenUserIsSoleOrganizerOfAnOrganization(
		CancellationToken cancellationToken)
	{
		var (_, ephemeralUsername, ephemeralPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		var org = await ephemeralClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Sole Organizer Test Org" }, cancellationToken);

		ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		var deleteAccount = () => ephemeralClient.DeleteMyAccountAsync(cancellationToken);

		var ex = await deleteAccount.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(409);

		var token = await fixture.GetAccessTokenAsync(ephemeralUsername, ephemeralPassword);
		token.Should().NotBeNullOrEmpty();
		var stillThere = await ephemeralClient.GetOrganizationDetailsAsync(org.Id.Value, cancellationToken);
		stillThere.Name.Should().Be("Sole Organizer Test Org");
	}

	[Test]
	public async Task DeleteVolunteerOpportunity_ShouldSucceed_WhenAnAnonymizedEngagementIsCheckedIn(
		CancellationToken cancellationToken)
	{
		var (_, ephemeralUsername, ephemeralPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var ephemeralClient = await fixture.CreateAuthenticatedClientAsync(ephemeralUsername, ephemeralPassword);

		await ephemeralClient.GetUserProfileAsync(cancellationToken);

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Anonymized Checked-In Engagement Test Org" }, cancellationToken);
		var opportunity = await olafClient.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Anonymized Checked-In Engagement Test Opportunity",
				DescriptionDe = "Integration test opportunity for #1724",
				OrganizationId = org.Id.Value,
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

		var engagement = await ephemeralClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		await ephemeralClient.DeleteMyAccountAsync(cancellationToken);

		var deleteOpportunity = () => olafClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);
		await deleteOpportunity.Should().NotThrowAsync(
			"an anonymized-but-checked-in engagement must not permanently block the deletion cascade (#1724)");

		(await fixture.CountRowsWhereAsync("volunteer_opportunity", "id", opportunity.Id))
			.Should().Be(0);

		(await fixture.CountRowsWhereAsync("engagement", "id", engagement.Id))
			.Should().Be(1);
	}
}
