namespace Domain.Users;

// The email types a user can opt out of. Cancellations are deliberately not here: a
// volunteer who opted out of them could turn up to a shift that no longer exists (#2402).
public enum EmailNotificationType
{
	NewSignUp,
	Withdrawal,
	EngagementConfirmed,
	EngagementReminder,
}
