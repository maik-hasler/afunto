using Domain.Users;

namespace Infrastructure.Persistence.Notifications;

// Queued by EngagementOrganizerNotificationHelper instead of sending an email immediately -
// OrganizerNotificationDigestJob periodically collapses every organizer's pending rows into
// one email, so N signups/withdrawals for the same organizer cost one send instead of N.
internal sealed class PendingOrganizerDigestItem
{
	public Guid Id { get; init; }

	public Guid OrganizerId { get; init; }

	// Where the digest's button links to. Null only for rows queued before #2402.
	public Guid? OrganizationId { get; init; }

	public string OpportunityTitle { get; init; } = string.Empty;

	public string VolunteerName { get; init; } = string.Empty;

	public EmailNotificationType Kind { get; init; }

	public DateTime OccurredOnUtc { get; init; }

	public DateTime? ClaimedOnUtc { get; set; }

	public DateTime? DigestSentOnUtc { get; set; }

	public static PendingOrganizerDigestItem Create(
		Guid organizerId,
		Guid organizationId,
		string opportunityTitle,
		string volunteerName,
		EmailNotificationType kind,
		DateTime occurredOnUtc) =>
		new()
		{
			Id = Guid.CreateVersion7(),
			OrganizerId = organizerId,
			OrganizationId = organizationId,
			OpportunityTitle = opportunityTitle,
			VolunteerName = volunteerName,
			Kind = kind,
			OccurredOnUtc = occurredOnUtc,
		};
}
