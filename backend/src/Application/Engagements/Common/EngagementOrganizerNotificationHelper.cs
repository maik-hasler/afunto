using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.Common;

// Queues one digest item per subscribed organizer instead of emailing them immediately -
// OrganizerNotificationDigestJob (Infrastructure/BackgroundJobs) periodically collapses each
// organizer's queued items into a single email, so an opportunity with many organizers or a
// volunteer signing up/withdrawing repeatedly doesn't cost one email per event.
internal static class EngagementOrganizerNotificationHelper
{
	public static async Task EnqueueAsync(
		IApplicationDbContext dbContext,
		IKeycloakOrganizationService keycloakOrganizationService,
		IKeycloakUserService keycloakUserService,
		EngagementId engagementId,
		VolunteerOpportunityId opportunityId,
		UserId volunteerId,
		EmailNotificationType subscriptionType,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(opportunityId, cancellationToken);
		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping organizer notification for opportunity {OpportunityId}: it no longer exists",
				opportunityId.Value);
			return;
		}

		KeycloakUserProfile volunteer;
		try
		{
			volunteer = await keycloakUserService.GetUserAsync(volunteerId.Value, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogWarning(
				ex,
				"Skipping organizer notification for engagement {EngagementId}: volunteer {VolunteerId} could not be looked up in Keycloak",
				engagementId.Value,
				volunteerId.Value);
			return;
		}

		var volunteerName = volunteer.FirstName ?? volunteer.Username;

		var members = await keycloakOrganizationService
			.GetMembersAsync(opportunity.OrganizationId.Value, cancellationToken);
		var organizerIds = members
			.Where(m => m.IsOrganisator)
			.Select(m => UserId.Create(m.UserId).GetValueOrThrow())
			.ToList();

		if (organizerIds.Count == 0)
			return;

		var organizerUsersById = (await dbContext.GetOrCreateUsersAsync(organizerIds, cancellationToken))
			.ToDictionary(u => u.Id);

		foreach (var organizerId in organizerIds)
		{
			var organizerUser = organizerUsersById[organizerId];

			if (!organizerUser.IsSubscribedTo(subscriptionType))
				continue;

			await dbContext.EnqueueOrganizerDigestItemAsync(
				organizerId, opportunity.OrganizationId, opportunity.TitleDe, volunteerName, subscriptionType, cancellationToken);
		}
	}
}
