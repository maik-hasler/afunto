using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements;
using Application.VolunteerOpportunities.Common;
using Domain.Notifications;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.UnpublishVolunteerOpportunity.v1;

internal sealed class VolunteerOpportunityUnpublishedDomainEventHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IEngagementReadRepository engagementReadRepository,
	ILogger<VolunteerOpportunityUnpublishedDomainEventHandler> logger)
	: INotificationHandler<VolunteerOpportunityUnpublishedDomainEvent>
{
	public async Task Handle(
		VolunteerOpportunityUnpublishedDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			notification.OpportunityId, cancellationToken);

		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping unpublish cascade for opportunity {OpportunityId}: it no longer exists",
				notification.OpportunityId.Value);
			return;
		}

		await VolunteerOpportunityEngagementCascadeHelper.NotifyAndCancelActiveEngagementsAsync(
			dbContext,
			engagementReadRepository,
			notification.OpportunityId,
			opportunity.TitleDe,
			NotificationKind.OpportunityUnpublished,
			engagementCancellationReason: null,
			notifyPerEngagement: true,
			logger,
			cancellationToken);

		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
