using Application.Organizations;
using Domain.Achievements;
using Domain.AuditLogs;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Common.Persistence;

public interface IApplicationDbContext
{
	IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> VolunteerOpportunities { get; }

	IAggregateRepository<Organization, OrganizationId> Organizations { get; }

	IAggregateRepository<Engagement, EngagementId> Engagements { get; }

	IAggregateRepository<Notification, NotificationId> Notifications { get; }

	IAggregateRepository<User, UserId> Users { get; }

	IAggregateRepository<Achievement, AchievementId> Achievements { get; }

	IAggregateRepository<OrganizationInvitation, OrganizationInvitationId> OrganizationInvitations { get; }

	IAggregateRepository<OrganizationMembership, OrganizationMembershipId> OrganizationMemberships { get; }

	IAggregateRepository<OrganizationDashboardLayout, OrganizationDashboardLayoutId> OrganizationDashboardLayouts { get; }

	IAggregateRepository<Report, ReportId> Reports { get; }

	IAggregateRepository<AuditLog, AuditLogId> AuditLogs { get; }

	Task DeleteReportsForReporterAsync(
		UserId reporterId,
		CancellationToken cancellationToken = default);

	Task<List<VolunteerOpportunity>> GetVolunteerOpportunitiesByIdsAsync(
		IReadOnlyCollection<VolunteerOpportunityId> opportunityIds,
		CancellationToken cancellationToken = default);

	Task<bool> IsOrganizerAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<bool> IsMemberAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<int> CountOrganizersAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<HashSet<Guid>> GetOrganizerUserIdsAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<Dictionary<Guid, OrganizationMemberRole>> GetMembershipRolesAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<OrganizationMembership?> GetMembershipAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<OrganizationDashboardLayout?> GetDashboardLayoutAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default);

	Task RemoveMembershipAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default);

	Task RemoveMembershipsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task RemoveDashboardLayoutsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task RemoveMembershipsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task RemoveDashboardLayoutsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task DeleteInvitationsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<List<Organization>> GetOrganizerOrganizationsAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<List<MemberOrganization>> GetMemberOrganizationsAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<List<OrganizationMembershipSummary>> GetMembershipsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<bool> TryCreateInvitationAsync(
		OrganizationInvitation invitation,
		CancellationToken cancellationToken = default);

	Task<Dictionary<Guid, string>> GetOrganizationNamesAsync(
		IReadOnlyCollection<OrganizationId> organizationIds,
		CancellationToken cancellationToken = default);

	Task<List<OrganizationInvitation>> GetInvitationsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<List<OrganizationInvitation>> GetPendingInvitationsForUserAsync(
		UserId inviteeId,
		CancellationToken cancellationToken = default);

	Task<bool> TryAwardAchievementAsync(
		Achievement achievement,
		CancellationToken cancellationToken = default);

	Task DeleteAchievementsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<bool> HasEngagementAsync(
		UserId volunteerId,
		VolunteerOpportunityId opportunityId,
		TimeSlotId? timeSlotId,
		CancellationToken cancellationToken = default);

	IAggregateRepository<UserStreak, UserStreakId> UserStreaks { get; }

	Task<UserStreak?> GetUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<UserStreak> GetOrCreateUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<int> CountUserStreaksAsync(
		CancellationToken cancellationToken = default);

	Task DeleteUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	ValueTask<List<Notification>> GetUnreadNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default);

	Task DeleteNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default);

	Task<int> DeleteReadNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default);

	Task DeleteInvitationReceivedNotificationsAsync(
		Guid invitationId,
		CancellationToken cancellationToken = default);

	Task<List<Engagement>> GetEngagementsForVolunteerTrackingAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default);

	Task<List<Engagement>> GetEngagementsByIdsAsync(
		IReadOnlyCollection<EngagementId> engagementIds,
		CancellationToken cancellationToken = default);

	Task<int> CountConfirmedEngagementsForVolunteerAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default);

	Task<int> CountActiveEngagementsForTimeSlotAsync(
		TimeSlotId timeSlotId,
		CancellationToken cancellationToken = default);

	Task<Dictionary<TimeSlotId, int>> CountActiveEngagementsForTimeSlotsAsync(
		IReadOnlyCollection<TimeSlotId> timeSlotIds,
		CancellationToken cancellationToken = default);

	Task LockTimeSlotForUpdateAsync(
		TimeSlotId timeSlotId,
		CancellationToken cancellationToken = default);

	Task<List<Engagement>> GetActiveEngagementsForOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		CancellationToken cancellationToken = default);

	Task<List<Engagement>> GetActiveEngagementsForTimeSlotsAsync(
		IReadOnlyCollection<TimeSlotId> timeSlotIds,
		CancellationToken cancellationToken = default);

	Task<List<VolunteerOpportunity>> GetBlockingOpportunitiesForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<List<VolunteerOpportunity>> GetOpportunitiesForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<bool> HasDuplicateReportAsync(
		ReportTargetType targetType,
		Guid targetId,
		UserId reporterId,
		CancellationToken cancellationToken = default);

	Task<List<Report>> GetOpenReportsForTargetAsync(
		ReportTargetType targetType,
		Guid targetId,
		CancellationToken cancellationToken = default);

	Task<List<Report>> GetReportHistoryForTargetAsync(
		ReportTargetType targetType,
		Guid targetId,
		CancellationToken cancellationToken = default);

	Task<Organization?> FindOrganizationIncludingDeletedAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default);

	Task<VolunteerOpportunity?> FindVolunteerOpportunityIncludingDeletedAsync(
		VolunteerOpportunityId opportunityId,
		CancellationToken cancellationToken = default);

	Task<User?> FindUserIncludingDeletedAsync(
		UserId userId,
		CancellationToken cancellationToken = default);

	Task<List<User>> GetOrCreateUsersAsync(
		IReadOnlyCollection<UserId> userIds,
		CancellationToken cancellationToken = default);

	Task<User> GetOrCreateUserAsync(
		UserId userId,
		string? preferredLanguage,
		CancellationToken cancellationToken = default);

	Task<Engagement?> GetTerminalEngagementAsync(
		UserId volunteerId,
		VolunteerOpportunityId opportunityId,
		TimeSlotId? timeSlotId,
		CancellationToken cancellationToken = default);

	Task<bool> CanConnectAsync(
		CancellationToken cancellationToken = default);

	// Marks each of the volunteer's engagements on this opportunity that is in `status` and
	// not yet notified (Engagement.StatusNotifiedAt) as notified - one conditional UPDATE per
	// row - and returns exactly the engagements this call won. Concurrent handlers for sibling
	// events therefore never put the same engagement into two emails.
	Task<List<Engagement>> ClaimStatusNotificationsAsync(
		UserId volunteerId,
		VolunteerOpportunityId opportunityId,
		EngagementStatus status,
		DateTimeOffset now,
		CancellationToken cancellationToken = default);

	// Undoes ClaimStatusNotificationsAsync after a failed send, so the outbox retry claims
	// (and emails) the same engagements again.
	Task ReleaseStatusNotificationsAsync(
		IReadOnlyCollection<EngagementId> engagementIds,
		CancellationToken cancellationToken = default);

	Task MarkRemindersSentAsync(
		IReadOnlyCollection<EngagementId> engagementIds,
		DateTimeOffset now,
		CancellationToken cancellationToken = default);

	Task EnqueueOrganizerDigestItemAsync(
		UserId organizerId,
		OrganizationId organizationId,
		string opportunityTitle,
		string volunteerName,
		EmailNotificationType kind,
		CancellationToken cancellationToken = default);
}
