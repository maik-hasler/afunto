using Application.Common.Exceptions;
using Application.Notifications;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

internal sealed class NotificationReadRepository(
	ApplicationDbContext dbContext)
	: INotificationReadRepository
{
	private static readonly NotificationKind[] EngagementKinds =
	[
		NotificationKind.EngagementCreated,
		NotificationKind.EngagementConfirmed,
		NotificationKind.EngagementCancelled,
		NotificationKind.EngagementWithdrawn,
		NotificationKind.FeedbackSubmitted,
		NotificationKind.MeetingProposed,
		NotificationKind.MeetingAccepted,
		NotificationKind.MeetingDeclined,
		NotificationKind.MeetingCancelled,
	];

	// Both sides of a meeting get these, so the link follows the recipient: the
	// volunteer lands on their sign-ups, an organizer on the opportunity's list.
	private static readonly NotificationKind[] MeetingKinds =
	[
		NotificationKind.MeetingProposed,
		NotificationKind.MeetingAccepted,
		NotificationKind.MeetingDeclined,
		NotificationKind.MeetingCancelled,
	];

	private static readonly NotificationKind[] InvitationKinds =
	[
		NotificationKind.InvitationReceived,
		NotificationKind.InvitationAccepted,
		NotificationKind.InvitationDeclined,
	];

	public async ValueTask<List<NotificationSummary>> GetByRecipientAsync(
		UserId recipientId,
		DateTimeOffset? before,
		Guid? beforeId,
		int limit,
		CancellationToken cancellationToken = default)
	{
		var recipientQuery = dbContext.NotificationsQuery
			.Where(n => n.RecipientId == recipientId);

		List<Notification> tiedWithCursor = [];
		if (before is not null && beforeId is not null)
		{
			var cursorBucketEnd = before.Value.AddMilliseconds(1);
			var cursorTiedBucket = await recipientQuery
				.Where(n => n.CreatedOn >= before.Value && n.CreatedOn < cursorBucketEnd)
				.ToListAsync(cancellationToken);

			tiedWithCursor = cursorTiedBucket
				.Where(n => n.Id.Value.CompareTo(beforeId.Value) < 0)
				.OrderByDescending(n => n.Id.Value)
				.ToList();
		}

		var olderQuery = before is not null
			? recipientQuery.Where(n => n.CreatedOn < before.Value)
			: recipientQuery;

		var older = await olderQuery
			.OrderByDescending(n => n.CreatedOn)
			.Take(limit)
			.ToListAsync(cancellationToken);

		var notifications = tiedWithCursor
			.Concat(older)
			.OrderByDescending(n => n.CreatedOn)
			.ThenByDescending(n => n.Id.Value)
			.Take(limit)
			.ToList();

		var engagementIds = notifications
			.Where(n => EngagementKinds.Contains(n.Kind))
			.Select(n => n.RelatedEntityId)
			.ToHashSet();

		var directOpportunityIds = notifications
			.Where(n => !EngagementKinds.Contains(n.Kind) && !InvitationKinds.Contains(n.Kind))
			.Select(n => n.RelatedEntityId)
			.ToHashSet();

		var invitationIds = notifications
			.Where(n => InvitationKinds.Contains(n.Kind))
			.Select(n => n.RelatedEntityId)
			.ToHashSet();

		Dictionary<Guid, string> invitationOrganizationNames = [];
		Dictionary<Guid, Guid> invitationOrganizationIds = [];
		if (invitationIds.Count > 0)
		{
			var invitationIdVOs = invitationIds.Select(id => OrganizationInvitationId.Create(id).GetValueOrThrow()).ToList();
			var invitationRows = await dbContext.OrganizationInvitationsQuery
				.Where(i => invitationIdVOs.Contains(i.Id))
				.Join(
					dbContext.OrganizationsQuery,
					i => i.OrganizationId,
					org => org.Id,
					(i, org) => new { i.Id, OrganizationId = org.Id, org.Name })
				.ToListAsync(cancellationToken);
			invitationOrganizationNames = invitationRows.ToDictionary(x => x.Id.Value, x => x.Name);
			invitationOrganizationIds = invitationRows.ToDictionary(x => x.Id.Value, x => x.OrganizationId.Value);
		}

		Dictionary<Guid, Guid> engagementToOpportunity = [];
		Dictionary<Guid, Guid?> engagementToVolunteer = [];
		if (engagementIds.Count > 0)
		{
			var engagementIdVOs = engagementIds.Select(id => EngagementId.Create(id).GetValueOrThrow()).ToList();
			var engagementRows = await dbContext.EngagementsQuery
				.Where(e => engagementIdVOs.Contains(e.Id))
				.Select(e => new { e.Id, e.OpportunityId, e.VolunteerId })
				.ToListAsync(cancellationToken);
			engagementToOpportunity = engagementRows.ToDictionary(x => x.Id.Value, x => x.OpportunityId.Value);
			engagementToVolunteer = engagementRows.ToDictionary(x => x.Id.Value, x => x.VolunteerId?.Value);
		}

		var opportunityIdsFromEngagements = engagementToOpportunity.Values.ToHashSet();
		var allOpportunityIds = opportunityIdsFromEngagements.Union(directOpportunityIds).ToHashSet();

		Dictionary<Guid, string> opportunityTitles = [];
		Dictionary<Guid, string?> opportunityTitlesEn = [];
		Dictionary<Guid, Guid> opportunityOrganizations = [];
		if (allOpportunityIds.Count > 0)
		{
			var opportunityIdVOs = allOpportunityIds.Select(id => VolunteerOpportunityId.Create(id).GetValueOrThrow()).ToList();
			var opportunityRows = await dbContext.VolunteerOpportunitiesQuery
				.Where(o => opportunityIdVOs.Contains(o.Id))
				.Select(o => new { o.Id, o.TitleDe, o.TitleEn, o.OrganizationId })
				.ToListAsync(cancellationToken);
			opportunityTitles = opportunityRows.ToDictionary(x => x.Id.Value, x => x.TitleDe);
			opportunityTitlesEn = opportunityRows.ToDictionary(x => x.Id.Value, x => x.TitleEn);
			opportunityOrganizations = opportunityRows.ToDictionary(x => x.Id.Value, x => x.OrganizationId.Value);
		}

		return notifications.Select(n =>
		{
			string? relatedTitle = null;
			string? relatedTitleEn = null;
			string? actionUrl = null;

			if (EngagementKinds.Contains(n.Kind) &&
				engagementToOpportunity.TryGetValue(n.RelatedEntityId, out var opportunityId))
			{
				opportunityTitles.TryGetValue(opportunityId, out relatedTitle);
				relatedTitle ??= n.TitleSnapshot;
				opportunityTitlesEn.TryGetValue(opportunityId, out relatedTitleEn);

				var isForOrganizer = MeetingKinds.Contains(n.Kind)
					? engagementToVolunteer.GetValueOrDefault(n.RelatedEntityId) != recipientId.Value
					: n.Kind is NotificationKind.EngagementCreated or NotificationKind.EngagementWithdrawn or NotificationKind.FeedbackSubmitted;

				actionUrl = isForOrganizer
					? (opportunityOrganizations.TryGetValue(opportunityId, out var organizationId)
						? $"/app/{organizationId}/dashboard/opportunities/{opportunityId}/engagements"
						: null)
					: "/my-signups";
			}
			else if (InvitationKinds.Contains(n.Kind))
			{
				invitationOrganizationNames.TryGetValue(n.RelatedEntityId, out relatedTitle);
				relatedTitleEn = relatedTitle;
				actionUrl = n.Kind == NotificationKind.InvitationReceived

					? "/my-signups"
					: (invitationOrganizationIds.TryGetValue(n.RelatedEntityId, out var invitationOrganizationId)
						? $"/app/{invitationOrganizationId}/dashboard/members"
						: null);
			}
			else if (!EngagementKinds.Contains(n.Kind))
			{
				opportunityTitles.TryGetValue(n.RelatedEntityId, out relatedTitle);
				relatedTitle ??= n.TitleSnapshot;
				opportunityTitlesEn.TryGetValue(n.RelatedEntityId, out relatedTitleEn);

				actionUrl = n.Kind is NotificationKind.OpportunityUpdated or NotificationKind.MembersOnlyOpportunityPublished
					? $"/volunteer-opportunities/{n.RelatedEntityId}"
					: "/my-signups";
			}

			return new NotificationSummary(
				n.Id.Value,
				n.Kind.ToString(),
				relatedTitle,
				actionUrl,
				n.IsRead,
				n.CreatedOn,
				relatedTitleEn);
		}).ToList();
	}

	public async ValueTask<int> CountUnreadByRecipientAsync(
		UserId recipientId,
		CancellationToken cancellationToken = default) =>
		await dbContext.NotificationsQuery
			.CountAsync(n => n.RecipientId == recipientId && !n.IsRead, cancellationToken);
}
