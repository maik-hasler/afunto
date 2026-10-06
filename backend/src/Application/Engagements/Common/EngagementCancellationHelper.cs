using Application.Common.Exceptions;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.Common;

internal static class EngagementCancellationHelper
{
	public static async Task<bool> CancelAsync(
		IApplicationDbContext dbContext,
		Engagement engagement,
		string? reason,
		string opportunityTitle,
		bool notifyVolunteer,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		// An anonymized engagement's volunteer has already deleted their account
		// (Engagement.Anonymize()) - Cancel() refuses to touch it (by the same
		// IsAnonymized guard every other mutator uses) and there is no volunteer left to
		// notify, so both the state change and the notification below would be wrong to
		// attempt. Without this guard a single anonymized-but-active engagement (checked
		// in, then its volunteer's account deleted) would 409 every caller of this helper
		// forever, with no way to clear it (afunto#1724).
		if (engagement.IsAnonymized)
		{
			logger.LogInformation(
				"Skipping cancellation of engagement {EngagementId}: its volunteer has deleted their account, nothing left to cancel or notify.",
				engagement.Id.Value);
			return false;
		}

		engagement.Cancel(reason, opportunityTitle).ThrowIfFailure();

		if (!notifyVolunteer)
			return true;

		var notification = Notification.Create(
			engagement.VolunteerId!.Value,
			NotificationKind.EngagementCancelled,
			engagement.Id.Value,
			opportunityTitle);

		await dbContext.Notifications.AddAsync(notification, cancellationToken);
		return true;
	}
}
