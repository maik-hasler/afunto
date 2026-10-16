using Application.Common.Exceptions;
using AwesomeAssertions;
using Domain.Achievements;
using Domain.AuditLogs;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Infrastructure.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core.Interfaces;

using DomainOrganization = Domain.Organizations.Organization;
using DomainOrganizationId = Domain.Organizations.OrganizationId;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EnumCheckConstraintTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task Achievement_ShouldPersistAndReload_ValidType(
		CancellationToken cancellationToken)
	{
		var achievement = Achievement.Create(UserId.New(), AchievementType.Milestone, "key", "Name", "Description", DateTimeOffset.UtcNow);

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Achievements.AddAsync(achievement, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.Achievements.FindAsync(achievement.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Type.Should().Be(AchievementType.Milestone);
	}

	[Test]
	public async Task Achievement_Type_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var achievement = Achievement.Create(UserId.New(), AchievementType.Milestone, "key", "Name", "Description", DateTimeOffset.UtcNow);

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Achievements.AddAsync(achievement, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"achievement", "type", achievement.Id.Value, "ck_achievement_type_valid", cancellationToken);
	}

	[Test]
	public async Task AuditLog_ShouldPersistAndReload_ValidActionTypeAndSubjectType(
		CancellationToken cancellationToken)
	{
		var auditLog = AuditLog.Create(UserId.New(), AuditActionType.UserShadowDeleted, AuditSubjectType.User, Guid.NewGuid());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.AuditLogs.AddAsync(auditLog, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.AuditLogs.FindAsync(auditLog.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.ActionType.Should().Be(AuditActionType.UserShadowDeleted);
		reloaded.SubjectType.Should().Be(AuditSubjectType.User);
	}

	[Test]
	public async Task AuditLog_ActionType_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var auditLog = AuditLog.Create(UserId.New(), AuditActionType.UserShadowDeleted, AuditSubjectType.User, Guid.NewGuid());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.AuditLogs.AddAsync(auditLog, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"audit_log", "action_type", auditLog.Id.Value, "ck_audit_log_action_type_valid", cancellationToken);
	}

	[Test]
	public async Task AuditLog_SubjectType_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var auditLog = AuditLog.Create(UserId.New(), AuditActionType.UserShadowDeleted, AuditSubjectType.User, Guid.NewGuid());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.AuditLogs.AddAsync(auditLog, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"audit_log", "subject_type", auditLog.Id.Value, "ck_audit_log_subject_type_valid", cancellationToken);
	}

	[Test]
	public async Task Engagement_ShouldPersistAndReload_ValidStatus(
		CancellationToken cancellationToken)
	{
		var engagement = Engagement.CreateIndividualContact(VolunteerOpportunityId.New(), UserId.New(), "Message").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Engagements.AddAsync(engagement, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.Engagements.FindAsync(engagement.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Status.Should().Be(EngagementStatus.Pending);
	}

	[Test]
	public async Task Engagement_Status_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var engagement = Engagement.CreateIndividualContact(VolunteerOpportunityId.New(), UserId.New(), "Message").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Engagements.AddAsync(engagement, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"engagement", "status", engagement.Id.Value, "ck_engagement_status_valid", cancellationToken);
	}

	[Test]
	public async Task Engagement_ShouldPersistAndReload_MeetingAcrossResponseAndCancel(
		CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var engagement = Engagement.CreateIndividualContact(VolunteerOpportunityId.New(), UserId.New(), "Message").GetValueOrThrow();
		var meetingId = MeetingId.New();
		engagement.ProposeMeeting(meetingId, MeetingParty.Organization, now.AddDays(2), now.AddDays(2).AddHours(1), MeetingFormat.Online, "https://meet.example.org/abc", "Note", now)
			.IsSuccess.Should().BeTrue();

		await using (var writeContext = fixture.CreateApplicationDbContext())
		{
			await writeContext.Engagements.AddAsync(engagement, cancellationToken);
			await writeContext.SaveChangesAsync(cancellationToken);
		}

		// Answering replaces the owned Meeting instance; that has to reach the row as an update.
		await using (var respondContext = fixture.CreateApplicationDbContext())
		{
			var tracked = await respondContext.Engagements.FindAsync(engagement.Id, cancellationToken);
			tracked!.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: true, now).IsSuccess.Should().BeTrue();
			await respondContext.SaveChangesAsync(cancellationToken);
		}

		await using (var readContext = fixture.CreateApplicationDbContext())
		{
			var reloaded = await readContext.Engagements.FindAsync(engagement.Id, cancellationToken);
			reloaded!.Meeting.Should().NotBeNull();
			reloaded.Meeting!.Id.Should().Be(meetingId);
			reloaded.Meeting.ProposedBy.Should().Be(MeetingParty.Organization);
			reloaded.Meeting.Response.Should().Be(MeetingResponse.Accepted);
			reloaded.Meeting.Format.Should().Be(MeetingFormat.Online);
			reloaded.Meeting.Location.Should().Be("https://meet.example.org/abc");
			reloaded.Meeting.Note.Should().Be("Note");
			reloaded.MeetingProposalCount.Should().Be(1);

			reloaded.CancelMeeting(meetingId, MeetingParty.Organization, now).IsSuccess.Should().BeTrue();
			await readContext.SaveChangesAsync(cancellationToken);
		}

		await using var finalContext = fixture.CreateApplicationDbContext();
		var cancelled = await finalContext.Engagements.FindAsync(engagement.Id, cancellationToken);
		cancelled!.Meeting.Should().BeNull();
	}

	[Test]
	public async Task Engagement_MeetingResponse_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var engagement = Engagement.CreateIndividualContact(VolunteerOpportunityId.New(), UserId.New(), "Message").GetValueOrThrow();
		engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Volunteer, now.AddDays(2), now.AddDays(2).AddHours(1), MeetingFormat.InPerson, "Vereinsheim", null, now);

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Engagements.AddAsync(engagement, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"engagement", "meeting_response", engagement.Id.Value, "ck_engagement_meeting_response_valid", cancellationToken);
		await AssertRejectsInvalidValueAsync(
			"engagement", "meeting_proposed_by", engagement.Id.Value, "ck_engagement_meeting_proposed_by_valid", cancellationToken);
		await AssertRejectsInvalidValueAsync(
			"engagement", "meeting_format", engagement.Id.Value, "ck_engagement_meeting_format_valid", cancellationToken);
	}

	[Test]
	public async Task Notification_ShouldPersistAndReload_ValidKind(
		CancellationToken cancellationToken)
	{
		var notification = Notification.Create(UserId.New(), NotificationKind.EngagementCreated, Guid.NewGuid());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Notifications.AddAsync(notification, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.Notifications.FindAsync(notification.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Kind.Should().Be(NotificationKind.EngagementCreated);
	}

	[Test]
	public async Task Notification_Kind_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var notification = Notification.Create(UserId.New(), NotificationKind.EngagementCreated, Guid.NewGuid());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Notifications.AddAsync(notification, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"notification", "kind", notification.Id.Value, "ck_notification_kind_valid", cancellationToken);
	}

	[Test]
	public async Task OrganizationInvitation_ShouldPersistAndReload_ValidIntendedRoleAndStatus(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var invitation = OrganizationInvitation.Create(
			organization.Id, UserId.New(), UserId.New(), OrganizationMemberRole.Organizer, DateTimeOffset.UtcNow);

		await writeContext.OrganizationInvitations.AddAsync(invitation, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.OrganizationInvitations.FindAsync(invitation.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.IntendedRole.Should().Be(OrganizationMemberRole.Organizer);
		reloaded.Status.Should().Be(InvitationStatus.Pending);
	}

	[Test]
	public async Task OrganizationInvitation_IntendedRole_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var invitation = OrganizationInvitation.Create(
			organization.Id, UserId.New(), UserId.New(), OrganizationMemberRole.Organizer, DateTimeOffset.UtcNow);

		await writeContext.OrganizationInvitations.AddAsync(invitation, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"organization_invitation", "intended_role", invitation.Id.Value, "ck_organization_invitation_intended_role_valid", cancellationToken);
	}

	[Test]
	public async Task OrganizationInvitation_Status_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var invitation = OrganizationInvitation.Create(
			organization.Id, UserId.New(), UserId.New(), OrganizationMemberRole.Organizer, DateTimeOffset.UtcNow);

		await writeContext.OrganizationInvitations.AddAsync(invitation, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"organization_invitation", "status", invitation.Id.Value, "ck_organization_invitation_status_valid", cancellationToken);
	}

	[Test]
	public async Task OrganizationMembership_ShouldPersistAndReload_ValidRole(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var membership = OrganizationMembership.Create(organization.Id, UserId.New(), OrganizationMemberRole.Member);

		await writeContext.OrganizationMemberships.AddAsync(membership, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.OrganizationMemberships.FindAsync(membership.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Role.Should().Be(OrganizationMemberRole.Member);
	}

	[Test]
	public async Task OrganizationMembership_Role_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var membership = OrganizationMembership.Create(organization.Id, UserId.New(), OrganizationMemberRole.Member);

		await writeContext.OrganizationMemberships.AddAsync(membership, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"organization_membership", "role", membership.Id.Value, "ck_organization_membership_role_valid", cancellationToken);
	}

	[Test]
	public async Task Report_ShouldPersistAndReload_ValidTargetTypeReasonAndStatus(
		CancellationToken cancellationToken)
	{
		var report = Report.Create(ReportTargetType.User, Guid.NewGuid(), UserId.New(), ReportReason.Spam, "Details").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Reports.AddAsync(report, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.Reports.FindAsync(report.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.TargetType.Should().Be(ReportTargetType.User);
		reloaded.Reason.Should().Be(ReportReason.Spam);
		reloaded.Status.Should().Be(ReportStatus.Open);
	}

	[Test]
	public async Task Report_TargetType_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var report = Report.Create(ReportTargetType.User, Guid.NewGuid(), UserId.New(), ReportReason.Spam, "Details").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Reports.AddAsync(report, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"report", "target_type", report.Id.Value, "ck_report_target_type_valid", cancellationToken);
	}

	[Test]
	public async Task Report_Reason_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var report = Report.Create(ReportTargetType.User, Guid.NewGuid(), UserId.New(), ReportReason.Spam, "Details").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Reports.AddAsync(report, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"report", "reason", report.Id.Value, "ck_report_reason_valid", cancellationToken);
	}

	[Test]
	public async Task Report_Status_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var report = Report.Create(ReportTargetType.User, Guid.NewGuid(), UserId.New(), ReportReason.Spam, "Details").GetValueOrThrow();

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Reports.AddAsync(report, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"report", "status", report.Id.Value, "ck_report_status_valid", cancellationToken);
	}

	[Test]
	public async Task User_ShouldPersistAndReload_ValidPreferredContact(
		CancellationToken cancellationToken)
	{
		var user = User.Create(UserId.New());
		user.SetPreferredContact(PreferredContact.Phone);

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Users.AddAsync(user, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.Users.FindAsync(user.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.PreferredContact.Should().Be(PreferredContact.Phone);
	}

	[Test]
	public async Task User_PreferredContact_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		var user = User.Create(UserId.New());

		await using var writeContext = fixture.CreateApplicationDbContext();
		await writeContext.Users.AddAsync(user, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"user", "preferred_contact", user.Id.Value, "ck_user_preferred_contact_valid", cancellationToken);
	}

	[Test]
	public async Task VolunteerOpportunity_ShouldPersistAndReload_ValidEnumValues(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.VolunteerOpportunities.FindAsync(opportunity.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Occurrence.Should().Be(Occurrence.OneTime);
		reloaded.ParticipationType.Should().Be(ParticipationType.IndividualContact);
		reloaded.CheckInMethod.Should().Be(CheckInMethod.None);
		reloaded.Category.Should().Be(Category.Social);
		reloaded.Status.Should().Be(OpportunityStatus.Draft);
	}

	[Test]
	public async Task VolunteerOpportunity_Category_ShouldAllowNull(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);

		var opportunity = VolunteerOpportunity.Create(
			organization.Id,
			"Title",
			null,
			"Description",
			null,
			isRemote: true,
			address: null,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			new RandomPinGenerator(),
			category: null,
			status: OpportunityStatus.Draft).GetValueOrThrow();

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await using var readContext = fixture.CreateApplicationDbContext();
		var reloaded = await readContext.VolunteerOpportunities.FindAsync(opportunity.Id, cancellationToken);

		reloaded.Should().NotBeNull();
		reloaded!.Category.Should().BeNull();
	}

	[Test]
	public async Task VolunteerOpportunity_Occurrence_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"volunteer_opportunity", "occurrence", opportunity.Id.Value, "ck_volunteer_opportunity_occurrence_valid", cancellationToken);
	}

	[Test]
	public async Task VolunteerOpportunity_ParticipationType_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"volunteer_opportunity", "participation_type", opportunity.Id.Value, "ck_volunteer_opportunity_participation_type_valid", cancellationToken);
	}

	[Test]
	public async Task VolunteerOpportunity_CheckInMethod_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"volunteer_opportunity", "check_in_method", opportunity.Id.Value, "ck_volunteer_opportunity_check_in_method_valid", cancellationToken);
	}

	[Test]
	public async Task VolunteerOpportunity_Category_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"volunteer_opportunity", "category", opportunity.Id.Value, "ck_volunteer_opportunity_category_valid", cancellationToken);
	}

	[Test]
	public async Task VolunteerOpportunity_Status_ShouldReject_InvalidValue(
		CancellationToken cancellationToken)
	{
		await using var writeContext = fixture.CreateApplicationDbContext();
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"TestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		writeContext.Set<DomainOrganization>().Add(organization);
		var opportunity = CreateDraftOpportunity(organization.Id);

		await writeContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);
		await writeContext.SaveChangesAsync(cancellationToken);

		await AssertRejectsInvalidValueAsync(
			"volunteer_opportunity", "status", opportunity.Id.Value, "ck_volunteer_opportunity_status_valid", cancellationToken);
	}

	private static VolunteerOpportunity CreateDraftOpportunity(DomainOrganizationId organizationId) =>
		VolunteerOpportunity.Create(
			organizationId,
			"Title",
			null,
			"Description",
			null,
			isRemote: true,
			address: null,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			new RandomPinGenerator(),
			category: Category.Social,
			status: OpportunityStatus.Draft).GetValueOrThrow();

	// Bypasses the HasConversion<string>() value converter (which would reject an invalid
	// C# enum value before it ever reached the DB) with a raw UPDATE, to prove the
	// CHECK constraint itself - not just the converter - is what's guarding the column.
	private async Task AssertRejectsInvalidValueAsync(
		string table,
		string column,
		Guid id,
		string constraintName,
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		await using var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
		await connection.OpenAsync(cancellationToken);

		await using var cmd = new NpgsqlCommand(
			$"UPDATE \"{table}\" SET \"{column}\" = 'NotARealValue' WHERE id = @id", connection);
		cmd.Parameters.AddWithValue("id", id);

		var act = async () => await cmd.ExecuteNonQueryAsync(cancellationToken);

		var exception = await act.Should().ThrowAsync<PostgresException>();
		exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
		exception.Which.ConstraintName.Should().Be(constraintName);
	}
}
