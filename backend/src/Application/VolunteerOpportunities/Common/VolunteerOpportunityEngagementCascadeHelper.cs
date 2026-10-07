using Application.Common.Persistence;
using Application.Engagements;
using Application.Engagements.Common;
using Application.Notifications;
using Domain.Notifications;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.Common;

internal static class VolunteerOpportunityEngagementCascadeHelper
{
	public static async Task NotifyAndCancelActiveEngagementsAsync(
		IApplicationDbContext dbContext,
		IEngagementReadRepository engagementReadRepository,
		VolunteerOpportunityId opportunityId,
		string opportunityTitle,
		NotificationKind opportunityNotificationKind,
		string? engagementCancellationReason,
		bool notifyPerEngagement,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		await OpportunityNotificationHelper.NotifyActiveVolunteersAsync(
			dbContext,
			engagementReadRepository,
			opportunityId,
			opportunityNotificationKind,
			cancellationToken,
			opportunityTitle: opportunityTitle);

		var activeEngagements = await dbContext.GetActiveEngagementsForOpportunityAsync(
			opportunityId, cancellationToken);
		foreach (var engagement in activeEngagements)
		{
			await EngagementCancellationHelper.CancelAsync(
				dbContext,
				engagement,
				engagementCancellationReason,
				opportunityTitle,
				notifyPerEngagement,
				logger,
				cancellationToken);
		}
	}
}
