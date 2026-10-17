using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Notifications;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.PublishVolunteerOpportunity.v1;

// A members-only opportunity is invisible to everyone outside the organization, so the
// members would only find it by browsing. Tell the non-organizer members the moment it goes
// live; the organizers published it themselves (afunto#2406).
internal sealed class VolunteerOpportunityPublishedDomainEventHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IKeycloakOrganizationService keycloakOrganizationService,
	ILogger<VolunteerOpportunityPublishedDomainEventHandler> logger)
	: INotificationHandler<VolunteerOpportunityPublishedDomainEvent>
{
	public async Task Handle(
		VolunteerOpportunityPublishedDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			notification.OpportunityId, cancellationToken);

		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping members-only publish notification for opportunity {OpportunityId}: it no longer exists",
				notification.OpportunityId.Value);
			return;
		}

		if (!opportunity.MembersOnly || opportunity.Status != OpportunityStatus.Published)
			return;

		var members = await keycloakOrganizationService.GetMembersAsync(
			opportunity.OrganizationId.Value, cancellationToken);

		foreach (var member in members.Where(m => !m.IsOrganisator))
		{
			var created = Notification.Create(
				UserId.Create(member.UserId).GetValueOrThrow(),
				NotificationKind.MembersOnlyOpportunityPublished,
				opportunity.Id.Value,
				opportunity.TitleDe);

			await dbContext.Notifications.AddAsync(created, cancellationToken);
		}

		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
