namespace Domain.Notifications;

public enum NotificationKind
{
	EngagementCreated,
	EngagementConfirmed,
	EngagementCancelled,
	EngagementWithdrawn,
	OpportunityUpdated,
	OpportunityDeleted,
	OpportunityUnpublished,
	OpportunityCancelled,
	InvitationReceived,
	InvitationAccepted,
	InvitationDeclined,
	FeedbackSubmitted,
	MeetingProposed,
	MeetingAccepted,
	MeetingDeclined,
	MeetingCancelled,
	MembersOnlyOpportunityPublished
}
