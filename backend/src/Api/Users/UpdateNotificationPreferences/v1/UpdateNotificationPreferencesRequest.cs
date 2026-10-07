namespace Api.Users.UpdateNotificationPreferences.v1;

public sealed record UpdateNotificationPreferencesRequest(
	bool NotifyOnNewSignUp,
	bool NotifyOnWithdrawal,
	bool NotifyOnEngagementConfirmed,
	bool NotifyOnEngagementReminder);
