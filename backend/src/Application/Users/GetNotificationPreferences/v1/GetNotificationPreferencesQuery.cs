using Application.Common.Messaging;
using Domain.Users;

namespace Application.Users.GetNotificationPreferences.v1;

public sealed record GetNotificationPreferencesQuery(UserId UserId)
	: IQuery<NotificationPreferencesResponse>;

public sealed record NotificationPreferencesResponse(
	bool NotifyOnNewSignUp,
	bool NotifyOnWithdrawal,
	bool NotifyOnEngagementConfirmed,
	bool NotifyOnEngagementReminder);
