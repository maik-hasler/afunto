using Application.Common.Messaging;
using Domain.Users;

namespace Application.Users.UpdateNotificationPreferences.v1;

public sealed record UpdateNotificationPreferencesCommand(
	UserId UserId,
	bool NotifyOnNewSignUp,
	bool NotifyOnWithdrawal,
	bool NotifyOnEngagementConfirmed,
	bool NotifyOnEngagementReminder)
	: ICommand<bool>;
