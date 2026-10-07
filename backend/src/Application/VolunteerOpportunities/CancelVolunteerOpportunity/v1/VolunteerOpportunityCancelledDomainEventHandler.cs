using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements;
using Application.VolunteerOpportunities.Common;
using Domain.Notifications;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.CancelVolunteerOpportunity.v1;

internal sealed class VolunteerOpportunityCancelledDomainEventHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IEngagementReadRepository engagementReadRepository,
	ILogger<VolunteerOpportunityCancelledDomainEventHandler> logger)
	: INotificationHandler<VolunteerOpportunityCancelledDomainEvent>
{
	public async Task Handle(
		VolunteerOpportunityCancelledDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			notification.OpportunityId, cancellationToken);

		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping cancel cascade for opportunity {OpportunityId}: it no longer exists",
				notification.OpportunityId.Value);
			return;
		}

		// The organizer's own words only, never a canned English sentence: the reason is
		// shown verbatim in the volunteer's email and on /my-signups, in either language.
		var engagementCancellationReason = string.IsNullOrWhiteSpace(notification.Reason)
			? null
			: notification.Reason;

		await VolunteerOpportunityEngagementCascadeHelper.NotifyAndCancelActiveEngagementsAsync(
			dbContext,
			engagementReadRepository,
			notification.OpportunityId,
			opportunity.TitleDe,
			NotificationKind.OpportunityCancelled,
			engagementCancellationReason,

			notifyPerEngagement: false,
			logger,
			cancellationToken);

		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
