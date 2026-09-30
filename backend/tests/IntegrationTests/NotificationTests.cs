using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class NotificationTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetMyNotifications_EngagementCreated_HasRelatedTitleAndDeepLink(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Deep-Link Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");
		notification.RelatedTitle.Should().Be(opportunityTitle);
		notification.ActionUrl.Should()
			.Be($"/app/{orgId}/dashboard/opportunities/{opportunity.Id}/engagements");
	}

	[Test]
	public async Task GetMyNotifications_EngagementCreated_HasRelatedTitleEn_WhenOpportunityHasEnglishTitle(
		CancellationToken cancellationToken)
	{
		const string opportunityTitleDe = "Notification Deep-Link Test (DE)";
		const string opportunityTitleEn = "Notification Deep-Link Test (EN)";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(
			olafClient, orgId, opportunityTitleDe, cancellationToken, opportunityTitleEn);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");
		notification.RelatedTitle.Should().Be(opportunityTitleDe);
		notification.RelatedTitleEn.Should().Be(opportunityTitleEn);
	}

	[Test]
	public async Task GetMyNotifications_EngagementCreated_DropsTitleAndDeepLink_AfterOpportunityDeleted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(
			olafClient, orgId, "Notification Deleted-Opportunity Test", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Please let me help." },
			cancellationToken);

		await olafClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var notifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = notifications.Items.Single(n => n.Kind == "EngagementCreated");
		notification.RelatedTitle.Should().BeNull(
			"the opportunity backing this notification no longer exists, so its title "
			+ "can no longer be resolved");
		notification.ActionUrl.Should().BeNull(
			"the opportunity's organization can no longer be resolved either, so no "
			+ "org-app deep link can be built");
	}

	[Test]
	public async Task GetMyNotifications_OpportunityDeleted_KeepsSnapshottedTitle_AfterOpportunityDeleted(
		CancellationToken cancellationToken)
	{
		const string OpportunityTitle = "Notification Snapshot-Title Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(
			olafClient, orgId, OpportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Please let me help." },
			cancellationToken);

		await olafClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraNotifications = await veraClient.GetMyNotificationsAsync(
			cancellationToken: cancellationToken);

		var notification = veraNotifications.Items.Single(n => n.Kind == "OpportunityDeleted");
		notification.RelatedTitle.Should().Be(OpportunityTitle,
			"the title was snapshotted onto the notification when it was created, "
			+ "before the opportunity row disappeared");
	}

	[Test]
	public async Task GetMyNotifications_EngagementConfirmed_HasRelatedTitleAndMyEngagementsUrl(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Confirm Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var veraNotifications = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = veraNotifications.Items.Single(n => n.Kind == "EngagementConfirmed");
		notification.RelatedTitle.Should().Be(opportunityTitle);
		notification.ActionUrl.Should().Be("/my-signups");
	}

	[Test]
	public async Task GetMyNotifications_EngagementCancelled_HasRelatedTitleAndMyEngagementsUrl(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Cancel Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.CancelEngagementAsync(engagement.Id, cancellationToken: cancellationToken);

		var veraNotifications = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = veraNotifications.Items.Single(n => n.Kind == "EngagementCancelled");
		notification.RelatedTitle.Should().Be(opportunityTitle);
		notification.ActionUrl.Should().Be("/my-signups");
	}

	[Test]
	public async Task GetMyNotifications_OpportunityCancelled_GivesTheVolunteerExactlyOneNotification(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Opportunity Cancel Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.CancelVolunteerOpportunityAsync(
			opportunity.Id,
			new CancelVolunteerOpportunityRequest { Reason = "Venue flooded" },
			cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.VolunteerOpportunities.VolunteerOpportunityCancelledDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the cancelled event within a few poll cycles");

		var veraNotifications = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = veraNotifications.Items.Should().ContainSingle().Which;
		notification.Kind.Should().Be("OpportunityCancelled");
		notification.RelatedTitle.Should().Be(opportunityTitle);
	}

	[Test]
	public async Task GetMyNotifications_EngagementWithdrawn_HasRelatedTitleAndOrganizerDashboardUrl(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Withdraw Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await veraClient.WithdrawEngagementAsync(engagement.Id, cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementWithdrawn");
		notification.RelatedTitle.Should().Be(opportunityTitle);
		notification.ActionUrl.Should()
			.Be($"/app/{orgId}/dashboard/opportunities/{opportunity.Id}/engagements");
	}

	[Test]
	public async Task GetMyNotifications_InvitationReceived_HasOrganizationNameAsRelatedTitleAndInvitationsUrl(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Notification Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		var veraNotifications = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = veraNotifications.Items.Single(n => n.Kind == "InvitationReceived");
		notification.RelatedTitle.Should().Be(organizationName);

		notification.ActionUrl.Should().Be("/my-signups");
	}

	[Test]
	public async Task GetMyNotifications_InvitationAccepted_HasOrganizationNameAsRelatedTitleAndMembersUrl(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Accepted Notification Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		await veraClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.Organizations.OrganizationInvitationAcceptedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the accepted event within a few poll cycles");

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "InvitationAccepted");
		notification.RelatedTitle.Should().Be(organizationName);
		notification.ActionUrl.Should().Be($"/app/{org.Id.Value}/dashboard/members");
	}

	[Test]
	public async Task GetMyNotifications_InvitationDeclined_HasOrganizationNameAsRelatedTitleAndMembersUrl(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Declined Notification Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		await veraClient.DeclineInvitationAsync(invitation.InvitationId, cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.Organizations.OrganizationInvitationDeclinedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the declined event within a few poll cycles");

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "InvitationDeclined");
		notification.RelatedTitle.Should().Be(organizationName);
		notification.ActionUrl.Should().Be($"/app/{org.Id.Value}/dashboard/members");
	}

	[Test]
	public async Task GetMyNotifications_InvitationReceived_IsRemoved_WhenInvitationIsAccepted(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Accept Cleanup Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		var beforeAccept = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		beforeAccept.Items.Should().Contain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);

		await veraClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);

		var afterAccept = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		afterAccept.Items.Should().NotContain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);
	}

	[Test]
	public async Task GetMyNotifications_InvitationReceived_IsRemoved_WhenInvitationIsDeclined(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Decline Cleanup Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		var beforeDecline = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		beforeDecline.Items.Should().Contain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);

		await veraClient.DeclineInvitationAsync(invitation.InvitationId, cancellationToken);

		var afterDecline = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		afterDecline.Items.Should().NotContain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);
	}

	[Test]
	public async Task GetMyNotifications_InvitationReceived_IsRemoved_WhenOrganizerDismissesInvitation(
		CancellationToken cancellationToken)
	{
		const string organizationName = "Invitation Dismiss Cleanup Test Org";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = organizationName }, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var invitation = await olafClient.CreateInvitationAsync(
			org.Id.Value, new CreateInvitationRequest { InviteeId = vera.Id, Role = "Member" }, cancellationToken);

		var beforeDismiss = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		beforeDismiss.Items.Should().Contain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);

		await olafClient.DismissInvitationAsync(org.Id.Value, invitation.InvitationId, cancellationToken);

		var afterDismiss = await veraClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		afterDismiss.Items.Should().NotContain(n => n.Kind == "InvitationReceived" && n.RelatedTitle == organizationName);
	}

	[Test]
	public async Task GetMyNotifications_FeedbackSubmitted_HasRelatedTitleAndOrganizerDashboardUrl(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Feedback Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 5, Comment = "Great experience" }, cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.Engagements.EngagementFeedbackSubmittedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the feedback event within a few poll cycles");

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);

		var notification = olafNotifications.Items.Single(n => n.Kind == "FeedbackSubmitted");
		notification.RelatedTitle.Should().Be(opportunityTitle);
		notification.ActionUrl.Should()
			.Be($"/app/{orgId}/dashboard/opportunities/{opportunity.Id}/engagements");
	}

	[Test]
	public async Task GetMyNotifications_BeforeCursor_ReturnsOnlyOlderNotifications(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunityOne = await CreateOpportunityAsync(olafClient, orgId, "Cursor Test One", cancellationToken);
		var opportunityTwo = await CreateOpportunityAsync(olafClient, orgId, "Cursor Test Two", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunityOne.Id, new CreateEngagementRequest { Message = "First" }, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunityTwo.Id, new CreateEngagementRequest { Message = "Second" }, cancellationToken);

		var firstPage = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var ordered = firstPage.Items
			.Where(n => n.Kind == "EngagementCreated")
			.OrderByDescending(n => n.CreatedOn)
			.ToList();
		ordered.Should().HaveCount(2);

		var secondPage = await olafClient.GetMyNotificationsAsync(ordered[0].CreatedOn.ToUnixTimeMilliseconds(), ordered[0].Id, cancellationToken);

		secondPage.Items.Should().ContainSingle(n => n.Id == ordered[1].Id);
		secondPage.Items.Should().NotContain(n => n.Id == ordered[0].Id);
		secondPage.HasMore.Should().BeFalse();
	}

	[Test]
	public async Task GetUnreadNotificationCount_ReflectsUnreadNotificationsAndMarkAllRead(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "Unread Count Test", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);

		var countBeforeRead = await olafClient.GetUnreadNotificationCountAsync(cancellationToken);
		countBeforeRead.Should().Be(1);

		await olafClient.MarkAllNotificationsReadAsync(cancellationToken);

		var countAfterRead = await olafClient.GetUnreadNotificationCountAsync(cancellationToken);
		countAfterRead.Should().Be(0);
	}

	[Test]
	public async Task MarkNotificationRead_ShouldReturn204AndFlagAsRead_WhenRequestingUserIsTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Mark Read Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");

		await olafClient.MarkNotificationReadAsync(notification.Id, cancellationToken);

		var updatedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		updatedNotifications.Items.Single(n => n.Id == notification.Id).IsRead.Should().BeTrue();
	}

	[Test]
	public async Task MarkNotificationRead_ShouldReturn404AndLeaveItUnread_WhenRequestingUserIsNotTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Cross-User Mark Read Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");

		var act = () => veraClient.MarkNotificationReadAsync(notification.Id, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(404);

		var unchangedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		unchangedNotifications.Items.Single(n => n.Id == notification.Id).IsRead.Should().BeFalse();
	}

	[Test]
	public async Task MarkNotificationUnread_ShouldReturn204AndFlagAsUnread_WhenRequestingUserIsTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Mark Unread Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");

		await olafClient.MarkNotificationReadAsync(notification.Id, cancellationToken);
		await olafClient.MarkNotificationUnreadAsync(notification.Id, cancellationToken);

		var updatedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		updatedNotifications.Items.Single(n => n.Id == notification.Id).IsRead.Should().BeFalse();
	}

	[Test]
	public async Task MarkNotificationUnread_ShouldReturn404AndLeaveItRead_WhenRequestingUserIsNotTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Cross-User Mark Unread Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");
		await olafClient.MarkNotificationReadAsync(notification.Id, cancellationToken);

		var act = () => veraClient.MarkNotificationUnreadAsync(notification.Id, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(404);

		var unchangedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		unchangedNotifications.Items.Single(n => n.Id == notification.Id).IsRead.Should().BeTrue();
	}

	[Test]
	public async Task DeleteNotification_ShouldReturn204AndRemoveIt_WhenRequestingUserIsTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Delete Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");

		await olafClient.DeleteNotificationAsync(notification.Id, cancellationToken);

		var updatedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		updatedNotifications.Items.Should().NotContain(n => n.Id == notification.Id);
	}

	[Test]
	public async Task DeleteNotification_ShouldReturn404AndLeaveIt_WhenRequestingUserIsNotTheRecipient(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Notification Cross-User Delete Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");

		var act = () => veraClient.DeleteNotificationAsync(notification.Id, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(404);

		var unchangedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		unchangedNotifications.Items.Should().Contain(n => n.Id == notification.Id);
	}

	[Test]
	public async Task DeleteReadNotifications_ShouldRemoveOnlyReadNotifications_ForTheRequestingUser(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunityOne = await CreateOpportunityAsync(olafClient, orgId, "Delete Read Test One", cancellationToken);
		var opportunityTwo = await CreateOpportunityAsync(olafClient, orgId, "Delete Read Test Two", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunityOne.Id, new CreateEngagementRequest { Message = "First" }, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunityTwo.Id, new CreateEngagementRequest { Message = "Second" }, cancellationToken);

		var notifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var toMarkRead = notifications.Items.First(n => n.Kind == "EngagementCreated");
		var toLeaveUnread = notifications.Items.Last(n => n.Kind == "EngagementCreated" && n.Id != toMarkRead.Id);
		await olafClient.MarkNotificationReadAsync(toMarkRead.Id, cancellationToken);

		await olafClient.DeleteReadNotificationsAsync(cancellationToken);

		var remaining = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		remaining.Items.Should().NotContain(n => n.Id == toMarkRead.Id);
		remaining.Items.Should().Contain(n => n.Id == toLeaveUnread.Id);
	}

	[Test]
	public async Task DeleteReadNotifications_ShouldNotAffectAnotherUsersReadNotifications(
		CancellationToken cancellationToken)
	{
		const string opportunityTitle = "Delete Read Cross-User Test";

		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, opportunityTitle, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var olafNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		var notification = olafNotifications.Items.Single(n => n.Kind == "EngagementCreated");
		await olafClient.MarkNotificationReadAsync(notification.Id, cancellationToken);

		await veraClient.DeleteReadNotificationsAsync(cancellationToken);

		var unchangedNotifications = await olafClient.GetMyNotificationsAsync(cancellationToken: cancellationToken);
		unchangedNotifications.Items.Should().Contain(n => n.Id == notification.Id);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"NotifTestOrg_{Guid.NewGuid()}" },
			cancellationToken);
		return org.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, string title, CancellationToken cancellationToken, string? titleEn = null)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = title,
				TitleEn = titleEn,
				DescriptionDe = "Integration test opportunity for notifications",
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
