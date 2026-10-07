namespace Application.Common.Email;

// One entry per email Afunto still sends. The set is deliberately small (#2402): an
// email goes out only when someone else decided or changed something the recipient
// needs to know to show up, or when the recipient has to act. A volunteer's own
// actions (signing up, expressing interest) get no email - the app and the bell
// already confirm them.
public enum EmailTemplateKind
{
	EngagementConfirmed,
	InterestConfirmed,
	EngagementCancelled,
	InterestCancelled,
	EngagementReminder,
	TimeSlotRescheduled,
	OpportunityLocationChanged,
	InvitationReceived,
	OrganizerDigest,
}
