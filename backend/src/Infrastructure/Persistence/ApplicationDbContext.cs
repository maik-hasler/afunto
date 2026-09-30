using System.Reflection;
using Application.Common.Persistence;
using Application.Organizations;
using Domain.Achievements;
using Domain.AuditLogs;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Infrastructure.Persistence.Notifications;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

internal sealed class ApplicationDbContext(
	DbContextOptions<ApplicationDbContext> options)
	: DbContext(options),
	IUnitOfWork,
	IApplicationDbContext
{
	public IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> VolunteerOpportunities
		=> new AggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>(
			Set<VolunteerOpportunity>(),
			Set<VolunteerOpportunity>().Include(vo => vo.TimeSlots),
			vo => vo.Id);

	internal IQueryable<VolunteerOpportunity> VolunteerOpportunitiesQuery => Set<VolunteerOpportunity>().AsNoTracking();

	internal IQueryable<TimeSlot> TimeSlotsQuery => Set<TimeSlot>().AsNoTracking();

	public IAggregateRepository<Organization, OrganizationId> Organizations
		=> new AggregateRepository<Organization, OrganizationId>(
			Set<Organization>(),
			Set<Organization>(),
			org => org.Id);

	internal IQueryable<Organization> OrganizationsQuery => Set<Organization>().AsNoTracking();

	public IAggregateRepository<Engagement, EngagementId> Engagements
		=> new AggregateRepository<Engagement, EngagementId>(
			Set<Engagement>(),
			Set<Engagement>(),
			e => e.Id);

	internal IQueryable<Engagement> EngagementsQuery => Set<Engagement>().AsNoTracking();

	public IAggregateRepository<Notification, NotificationId> Notifications
		=> new AggregateRepository<Notification, NotificationId>(
			Set<Notification>(),
			Set<Notification>(),
			n => n.Id);

	public IAggregateRepository<User, UserId> Users
		=> new AggregateRepository<User, UserId>(
			Set<User>(),
			Set<User>(),
			u => u.Id);

	internal IQueryable<User> UsersQuery => Set<User>().AsNoTracking();

	internal IQueryable<Notification> NotificationsQuery => Set<Notification>().AsNoTracking();

	public IAggregateRepository<Achievement, AchievementId> Achievements
		=> new AggregateRepository<Achievement, AchievementId>(
			Set<Achievement>(),
			Set<Achievement>(),
			a => a.Id);

	internal IQueryable<Achievement> AchievementsQuery => Set<Achievement>().AsNoTracking();

	public IAggregateRepository<OrganizationInvitation, OrganizationInvitationId> OrganizationInvitations
		=> new AggregateRepository<OrganizationInvitation, OrganizationInvitationId>(
			Set<OrganizationInvitation>(),
			Set<OrganizationInvitation>(),
			i => i.Id);

	internal IQueryable<OrganizationInvitation> OrganizationInvitationsQuery => Set<OrganizationInvitation>().AsNoTracking();

	public IAggregateRepository<OrganizationMembership, OrganizationMembershipId> OrganizationMemberships
		=> new AggregateRepository<OrganizationMembership, OrganizationMembershipId>(
			Set<OrganizationMembership>(),
			Set<OrganizationMembership>(),
			m => m.Id);

	internal IQueryable<OrganizationMembership> OrganizationMembershipsQuery => Set<OrganizationMembership>().AsNoTracking();

	public IAggregateRepository<OrganizationDashboardLayout, OrganizationDashboardLayoutId> OrganizationDashboardLayouts
		=> new AggregateRepository<OrganizationDashboardLayout, OrganizationDashboardLayoutId>(
			Set<OrganizationDashboardLayout>(),
			Set<OrganizationDashboardLayout>(),
			l => l.Id);

	public IAggregateRepository<Report, ReportId> Reports
		=> new AggregateRepository<Report, ReportId>(
			Set<Report>(),
			Set<Report>(),
			r => r.Id);

	internal IQueryable<Report> ReportsQuery => Set<Report>().AsNoTracking();

	public IAggregateRepository<AuditLog, AuditLogId> AuditLogs
		=> new AggregateRepository<AuditLog, AuditLogId>(
			Set<AuditLog>(),
			Set<AuditLog>(),
			a => a.Id);

	internal IQueryable<AuditLog> AuditLogsQuery => Set<AuditLog>().AsNoTracking();

	public async Task DeleteReportsForReporterAsync(
		UserId reporterId,
		CancellationToken cancellationToken = default) =>
		await Set<Report>()
			.Where(r => r.ReporterId == reporterId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task<List<VolunteerOpportunity>> GetVolunteerOpportunitiesByIdsAsync(
		IReadOnlyCollection<VolunteerOpportunityId> opportunityIds,
		CancellationToken cancellationToken = default) =>
		await Set<VolunteerOpportunity>()
			.Where(vo => opportunityIds.Contains(vo.Id))
			.ToListAsync(cancellationToken);

	public async Task<bool> IsOrganizerAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.AnyAsync(m => m.OrganizationId == organizationId
				&& m.UserId == userId
				&& m.Role == OrganizationMemberRole.Organizer, cancellationToken);

	public async Task<bool> IsMemberAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.AnyAsync(m => m.OrganizationId == organizationId
				&& m.UserId == userId, cancellationToken);

	public async Task<int> CountOrganizersAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.CountAsync(m => m.OrganizationId == organizationId
				&& m.Role == OrganizationMemberRole.Organizer, cancellationToken);

	public async Task<HashSet<Guid>> GetOrganizerUserIdsAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default)
	{
		var userIds = await Set<OrganizationMembership>()
			.AsNoTracking()
			.Where(m => m.OrganizationId == organizationId && m.Role == OrganizationMemberRole.Organizer)
			.Select(m => m.UserId)
			.ToListAsync(cancellationToken);

		return userIds.Select(id => id.Value).ToHashSet();
	}

	public async Task<Dictionary<Guid, OrganizationMemberRole>> GetMembershipRolesAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default)
	{
		var memberships = await Set<OrganizationMembership>()
			.AsNoTracking()
			.Where(m => m.OrganizationId == organizationId)
			.Select(m => new { m.UserId, m.Role })
			.ToListAsync(cancellationToken);

		return memberships.ToDictionary(m => m.UserId.Value, m => m.Role);
	}

	public async Task<OrganizationMembership?> GetMembershipAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);

	public async Task<OrganizationDashboardLayout?> GetDashboardLayoutAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationDashboardLayout>()
			.FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.UserId == userId, cancellationToken);

	public async Task RemoveMembershipAsync(
		OrganizationId organizationId,
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.Where(m => m.OrganizationId == organizationId && m.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task RemoveMembershipsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.Where(m => m.OrganizationId == organizationId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task RemoveDashboardLayoutsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationDashboardLayout>()
			.Where(l => l.OrganizationId == organizationId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task RemoveMembershipsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.Where(m => m.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task RemoveDashboardLayoutsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationDashboardLayout>()
			.Where(l => l.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task DeleteInvitationsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationInvitation>()
			.Where(i => i.InviteeId == userId || i.InvitedById == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task<List<Organization>> GetOrganizerOrganizationsAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationMembership>()
			.AsNoTracking()
			.Where(m => m.UserId == userId && m.Role == OrganizationMemberRole.Organizer)
			.Join(
				Set<Organization>().AsNoTracking(),
				m => m.OrganizationId,
				o => o.Id,
				(m, o) => o)
			.OrderBy(o => o.Name)
			.ToListAsync(cancellationToken);

	public async Task<List<MemberOrganization>> GetMemberOrganizationsAsync(
		UserId userId,
		CancellationToken cancellationToken = default)
	{
		var raw = await Set<OrganizationMembership>()
			.AsNoTracking()
			.Where(m => m.UserId == userId)
			.Join(
				Set<Organization>().AsNoTracking(),
				m => m.OrganizationId,
				o => o.Id,
				(m, o) => new { Organization = o, m.Role })
			.OrderBy(x => x.Organization.Name)
			.ToListAsync(cancellationToken);

		return raw
			.Select(x => new MemberOrganization(x.Organization, x.Role))
			.ToList();
	}

	public async Task<List<OrganizationMembershipSummary>> GetMembershipsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default)
	{
		var raw = await Set<OrganizationMembership>()
			.AsNoTracking()
			.Where(m => m.UserId == userId)
			.Join(
				Set<Organization>().AsNoTracking(),
				m => m.OrganizationId,
				o => o.Id,
				(m, o) => new { OrganizationId = o.Id.Value, OrganizationName = o.Name, m.Role })
			.OrderBy(s => s.OrganizationName)
			.ToListAsync(cancellationToken);

		return raw
			.Select(s => new OrganizationMembershipSummary(s.OrganizationId, s.OrganizationName, s.Role.ToString()))
			.ToList();
	}

	public async Task<bool> TryAwardAchievementAsync(
		Achievement achievement,
		CancellationToken cancellationToken = default)
	{
		var rows = await Database.ExecuteSqlInterpolatedAsync($@"
			INSERT INTO achievement (id, user_id, type, key, name, description, unlocked_at, created_on)
			VALUES (
				{achievement.Id.Value},
				{achievement.UserId.Value},
				{achievement.Type.ToString()},
				{achievement.Key},
				{achievement.Name},
				{achievement.Description},
				{achievement.UnlockedAt},
				{DateTimeOffset.UtcNow})
			ON CONFLICT (user_id, key) DO NOTHING", cancellationToken);

		return rows > 0;
	}

	public async Task DeleteAchievementsForUserAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<Achievement>()
			.Where(a => a.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task<bool> HasEngagementAsync(
		UserId volunteerId,
		VolunteerOpportunityId opportunityId,
		TimeSlotId? timeSlotId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.AnyAsync(e => e.VolunteerId == volunteerId
				&& e.OpportunityId == opportunityId
				&& e.TimeSlotId == timeSlotId
				&& e.Status != EngagementStatus.Withdrawn
				&& e.Status != EngagementStatus.Cancelled, cancellationToken);

	public IAggregateRepository<UserStreak, UserStreakId> UserStreaks
		=> new AggregateRepository<UserStreak, UserStreakId>(
			Set<UserStreak>(),
			Set<UserStreak>(),
			s => s.Id);

	internal IQueryable<UserStreak> UserStreaksQuery => Set<UserStreak>().AsNoTracking();

	public async Task<UserStreak?> GetUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<UserStreak>()
			.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

	public async Task<UserStreak> GetOrCreateUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default)
	{
		var existing = await Set<UserStreak>()
			.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
		if (existing is not null)
			return existing;

		await Database.ExecuteSqlInterpolatedAsync($@"
			INSERT INTO user_streak (id, user_id, login_streak, activity_streak, total_confirmed_engagements, created_on)
			VALUES ({UserStreakId.New().Value}, {userId.Value}, 0, 0, 0, {DateTimeOffset.UtcNow})
			ON CONFLICT (user_id) DO NOTHING", cancellationToken);

		return await Set<UserStreak>().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken)
			?? throw new InvalidOperationException($"UserStreak for user '{userId.Value}' was not found immediately after being inserted.");
	}

	public async Task<int> CountUserStreaksAsync(
		CancellationToken cancellationToken = default) =>
		await Set<UserStreak>().CountAsync(cancellationToken);

	public async Task DeleteUserStreakAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<UserStreak>()
			.Where(s => s.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken);

	public async ValueTask<List<Notification>> GetUnreadNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default) =>
		await Set<Notification>()
			.Where(n => n.RecipientId == recipientId && !n.IsRead)
			.ToListAsync(cancellationToken);

	public async Task DeleteNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default) =>
		await Set<Notification>()
			.Where(n => n.RecipientId == recipientId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task<int> DeleteReadNotificationsForRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default) =>
		await Set<Notification>()
			.Where(n => n.RecipientId == recipientId && n.IsRead)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task DeleteInvitationReceivedNotificationsAsync(
		Guid invitationId,
		CancellationToken cancellationToken = default) =>
		await Set<Notification>()
			.Where(n => n.Kind == NotificationKind.InvitationReceived && n.RelatedEntityId == invitationId)
			.ExecuteDeleteAsync(cancellationToken);

	public async Task<List<Engagement>> GetEngagementsForVolunteerTrackingAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.Where(e => e.VolunteerId == volunteerId)
			.ToListAsync(cancellationToken);

	public async Task<List<Engagement>> GetEngagementsByIdsAsync(
		IReadOnlyCollection<EngagementId> engagementIds,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.Where(e => engagementIds.Contains(e.Id))
			.ToListAsync(cancellationToken);

	public async Task<int> CountConfirmedEngagementsForVolunteerAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.CountAsync(e => e.VolunteerId == volunteerId && e.Status == EngagementStatus.Confirmed, cancellationToken);

	public async Task<int> CountActiveEngagementsForTimeSlotAsync(
		TimeSlotId timeSlotId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.CountAsync(e => e.TimeSlotId == timeSlotId
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed),
				cancellationToken);

	public async Task<Dictionary<TimeSlotId, int>> CountActiveEngagementsForTimeSlotsAsync(
		IReadOnlyCollection<TimeSlotId> timeSlotIds,
		CancellationToken cancellationToken = default)
	{
		var nullableIds = timeSlotIds.Select(id => (TimeSlotId?)id).ToList();

		var activeTimeSlotIds = await Set<Engagement>()
			.Where(e => nullableIds.Contains(e.TimeSlotId)
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
			.Select(e => e.TimeSlotId)
			.ToListAsync(cancellationToken);

		return activeTimeSlotIds
			.Where(id => id.HasValue)
			.GroupBy(id => id!.Value)
			.ToDictionary(g => g.Key, g => g.Count());
	}

	public async Task LockTimeSlotForUpdateAsync(
		TimeSlotId timeSlotId,
		CancellationToken cancellationToken = default) =>
		await Set<TimeSlot>()
			.FromSqlInterpolated($@"
				SELECT id, created_on, modified_on, end_date_time, max_participants, recurrence_count, recurrence_frequency, series_id, start_date_time, volunteer_opportunity_id
				FROM time_slot
				WHERE id = {timeSlotId.Value}
				FOR UPDATE")
			.ToListAsync(cancellationToken);

	public async Task<List<Engagement>> GetActiveEngagementsForOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.Where(e => e.OpportunityId == opportunityId
				&& e.VolunteerId != null
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
			.ToListAsync(cancellationToken);

	public async Task<List<Engagement>> GetActiveEngagementsForTimeSlotsAsync(
		IReadOnlyCollection<TimeSlotId> timeSlotIds,
		CancellationToken cancellationToken = default)
	{
		var nullableIds = timeSlotIds.Select(id => (TimeSlotId?)id).ToList();

		return await Set<Engagement>()
			.Where(e => nullableIds.Contains(e.TimeSlotId)
				&& e.VolunteerId != null
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
			.ToListAsync(cancellationToken);
	}

	public async Task<List<VolunteerOpportunity>> GetBlockingOpportunitiesForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default)
	{
		var opportunityIds = await Set<VolunteerOpportunity>()
			.Where(vo => vo.OrganizationId == organizationId)
			.Select(vo => vo.Id)
			.ToListAsync(cancellationToken);

		if (opportunityIds.Count == 0)
			return [];

		var now = DateTimeOffset.UtcNow;

		var opportunityIdsWithActiveEngagements = await Set<Engagement>()
			.Where(e => opportunityIds.Contains(e.OpportunityId)
				&& e.VolunteerId != null
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
			.Select(e => e.OpportunityId)
			.Distinct()
			.ToListAsync(cancellationToken);

		return await Set<VolunteerOpportunity>()
			.Where(vo => vo.OrganizationId == organizationId
				&& (vo.TimeSlots.Any(ts => ts.StartDateTime > now)
					|| opportunityIdsWithActiveEngagements.Contains(vo.Id)))
			.ToListAsync(cancellationToken);
	}

	public async Task<List<VolunteerOpportunity>> GetOpportunitiesForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<VolunteerOpportunity>()
			.Include(vo => vo.TimeSlots)
			.Where(vo => vo.OrganizationId == organizationId)
			.ToListAsync(cancellationToken);

	public async Task<bool> HasDuplicateReportAsync(
		ReportTargetType targetType,
		Guid targetId,
		UserId reporterId,
		CancellationToken cancellationToken = default)
	{
		// Open always blocks (still under review); Dismissed blocks indefinitely so a
		// reporter cannot force a moderator to re-adjudicate the same claim on a loop
		// (afunto#2212); anything else (Actioned) only blocks for a cooldown window,
		// so a target that reoffends after being actioned can still be re-reported later.
		var cutoff = DateTimeOffset.UtcNow.AddDays(-Report.DuplicateWindowDays);

		return await Set<Report>()
			.AnyAsync(r => r.TargetType == targetType
				&& r.TargetId == targetId
				&& r.ReporterId == reporterId
				&& (r.Status == ReportStatus.Open
					|| r.Status == ReportStatus.Dismissed
					|| r.CreatedOn >= cutoff), cancellationToken);
	}

	public async Task<List<Report>> GetOpenReportsForTargetAsync(
		ReportTargetType targetType,
		Guid targetId,
		CancellationToken cancellationToken = default) =>
		await Set<Report>()
			.Where(r => r.TargetType == targetType
				&& r.TargetId == targetId
				&& r.Status == ReportStatus.Open)
			.ToListAsync(cancellationToken);

	public async Task<List<Report>> GetReportHistoryForTargetAsync(
		ReportTargetType targetType,
		Guid targetId,
		CancellationToken cancellationToken = default) =>
		await Set<Report>()
			.Where(r => r.TargetType == targetType && r.TargetId == targetId)
			.OrderByDescending(r => r.CreatedOn)
			.ToListAsync(cancellationToken);

	public async Task<Organization?> FindOrganizationIncludingDeletedAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<Organization>()
			.IgnoreQueryFilters()
			.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

	public async Task<VolunteerOpportunity?> FindVolunteerOpportunityIncludingDeletedAsync(
		VolunteerOpportunityId opportunityId,
		CancellationToken cancellationToken = default) =>
		await Set<VolunteerOpportunity>()
			.IgnoreQueryFilters()
			.Include(vo => vo.TimeSlots)
			.FirstOrDefaultAsync(vo => vo.Id == opportunityId, cancellationToken);

	public async Task<User?> FindUserIncludingDeletedAsync(
		UserId userId,
		CancellationToken cancellationToken = default) =>
		await Set<User>()
			.IgnoreQueryFilters()
			.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

	public async Task<List<User>> GetOrCreateUsersAsync(
		IReadOnlyCollection<UserId> userIds,
		CancellationToken cancellationToken = default)
	{
		var existing = await Set<User>()
			.IgnoreQueryFilters()
			.Where(u => userIds.Contains(u.Id))
			.ToListAsync(cancellationToken);

		var missingIds = userIds.Except(existing.Select(u => u.Id));
		var created = missingIds.Select(User.Create).ToList();

		if (created.Count > 0)
			await Set<User>().AddRangeAsync(created, cancellationToken);

		return [.. existing, .. created];
	}

	public async Task<User> GetOrCreateUserAsync(
		UserId userId,
		string? preferredLanguage,
		CancellationToken cancellationToken = default)
	{
		var existing = await Set<User>()
			.IgnoreQueryFilters()
			.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
		if (existing is not null)
			return existing;

		await Database.ExecuteSqlInterpolatedAsync($@"
			INSERT INTO ""user"" (id, languages, skills, preferred_language)
			VALUES ({userId.Value}, '[]', '[]', {preferredLanguage})
			ON CONFLICT (id) DO NOTHING", cancellationToken);

		return await Set<User>()
			.IgnoreQueryFilters()
			.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
			?? throw new InvalidOperationException($"User '{userId.Value}' was not found immediately after being inserted.");
	}

	public async Task<Engagement?> GetTerminalEngagementAsync(
		UserId volunteerId,
		VolunteerOpportunityId opportunityId,
		TimeSlotId? timeSlotId,
		CancellationToken cancellationToken = default) =>
		await Set<Engagement>()
			.FirstOrDefaultAsync(e => e.VolunteerId == volunteerId
				&& e.OpportunityId == opportunityId
				&& e.TimeSlotId == timeSlotId
				&& (e.Status == EngagementStatus.Withdrawn || e.Status == EngagementStatus.Cancelled),
				cancellationToken);

	public async Task<bool> TryCreateInvitationAsync(
		OrganizationInvitation invitation,
		CancellationToken cancellationToken = default)
	{
		var rows = await Database.ExecuteSqlInterpolatedAsync($@"
			INSERT INTO organization_invitation
				(id, organization_id, invitee_id, invited_by_id, intended_role, status, expires_on, created_on)
			VALUES (
				{invitation.Id.Value},
				{invitation.OrganizationId.Value},
				{invitation.InviteeId.Value},
				{invitation.InvitedById.Value},
				{invitation.IntendedRole.ToString()},
				{invitation.Status.ToString()},
				{invitation.ExpiresOn},
				{DateTimeOffset.UtcNow})
			ON CONFLICT (organization_id, invitee_id) WHERE status = 'Pending' DO NOTHING", cancellationToken);

		return rows > 0;
	}

	public async Task<Dictionary<Guid, string>> GetOrganizationNamesAsync(
		IReadOnlyCollection<OrganizationId> organizationIds,
		CancellationToken cancellationToken = default) =>
		await Set<Organization>()
			.Where(o => organizationIds.Contains(o.Id))
			.Select(o => new { o.Id, o.Name })
			.ToDictionaryAsync(x => x.Id.Value, x => x.Name, cancellationToken);

	public async Task<List<OrganizationInvitation>> GetInvitationsForOrganizationAsync(
		OrganizationId organizationId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationInvitation>()
			.Where(i => i.OrganizationId == organizationId && i.Status != InvitationStatus.Accepted)
			.OrderByDescending(i => i.CreatedOn)
			.ToListAsync(cancellationToken);

	public async Task<List<OrganizationInvitation>> GetPendingInvitationsForUserAsync(
		UserId inviteeId,
		CancellationToken cancellationToken = default) =>
		await Set<OrganizationInvitation>()
			.Where(i => i.InviteeId == inviteeId && i.Status == InvitationStatus.Pending)
			.OrderByDescending(i => i.CreatedOn)
			.ToListAsync(cancellationToken);

	public Task<bool> CanConnectAsync(
		CancellationToken cancellationToken = default) =>
		Database.CanConnectAsync(cancellationToken);

	public async Task EnqueueOrganizerDigestItemAsync(
		UserId organizerId,
		string opportunityTitle,
		string volunteerName,
		EmailNotificationType kind,
		CancellationToken cancellationToken = default) =>
		await Set<PendingOrganizerDigestItem>().AddAsync(
			PendingOrganizerDigestItem.Create(
				organizerId.Value, opportunityTitle, volunteerName, kind, DateTime.UtcNow),
			cancellationToken);

	protected override void OnModelCreating(
		ModelBuilder modelBuilder) =>
			modelBuilder.ApplyConfigurationsFromAssembly(
				Assembly.GetExecutingAssembly());

	public bool HasActiveTransaction =>
		Database.CurrentTransaction != null;

	public async Task<TResult> ExecuteInTransactionAsync<TResult>(
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken = default)
	{
		var strategy = Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync<TResult>(async ct =>
		{
			await using var transaction = await Database.BeginTransactionAsync(ct);

			try
			{
				TResult result = await operation(ct);

				await transaction.CommitAsync(ct);

				return result;
			}
			catch (Exception)
			{
				await transaction.RollbackAsync(ct);

				throw;
			}
		}, cancellationToken);
	}
}
