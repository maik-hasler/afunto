using Application.Common.Exceptions;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.Common;

/// <summary>
/// Loads an engagement for a volunteer acting on its meeting, together with the
/// opportunity whose organizers hear about it. Same ownership rule as withdrawing.
/// </summary>
internal static class VolunteerMeetingAccess
{
	public static async Task<(Engagement Engagement, VolunteerOpportunity Opportunity)> LoadOwnAsync(
		IApplicationDbContext dbContext,
		EngagementId engagementId,
		Guid volunteerId,
		CancellationToken cancellationToken)
	{
		var engagement = await dbContext.Engagements.FindAsync(engagementId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("Engagement.NotFound", $"Engagement '{engagementId.Value}' not found."));

		if (engagement.IsAnonymized)
			throw new ResultFailureException(Error.Conflict("Engagement.Anonymized", "This engagement's volunteer account has been deleted."));

		if (engagement.VolunteerId!.Value.Value != volunteerId)
			throw new ResultFailureException(Error.Forbidden("Engagement.NotOwner", "Only the volunteer who created this engagement can do this."));

		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(engagement.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{engagement.OpportunityId.Value}' not found."));

		return (engagement, opportunity);
	}
}
