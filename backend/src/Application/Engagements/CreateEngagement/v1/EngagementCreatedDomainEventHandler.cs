using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.CreateEngagement.v1;

internal sealed class EngagementCreatedDomainEventHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IKeycloakOrganizationService keycloakOrganizationService,
	IKeycloakUserService keycloakUserService,
	ILogger<EngagementCreatedDomainEventHandler> logger)
	: INotificationHandler<EngagementCreatedDomainEvent>
{
	public async Task Handle(
		EngagementCreatedDomainEvent notification,
		CancellationToken cancellationToken)
	{
		await EngagementOrganizerNotificationHelper.EnqueueAsync(
			dbContext,
			keycloakOrganizationService,
			keycloakUserService,
			notification.EngagementId,
			notification.OpportunityId,
			notification.VolunteerId,
			EmailNotificationType.NewSignUp,
			logger,
			cancellationToken);

		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
