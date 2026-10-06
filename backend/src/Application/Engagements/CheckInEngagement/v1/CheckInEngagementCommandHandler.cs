using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Primitives;

namespace Application.Engagements.CheckInEngagement.v1;

internal sealed class CheckInEngagementCommandHandler(
	IApplicationDbContext dbContext)
	: ICommandHandler<CheckInEngagementCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		CheckInEngagementCommand request,
		CancellationToken cancellationToken = default)
	{
		var engagement = await dbContext.Engagements.FindAsync(request.EngagementId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("Engagement.NotFound", $"Engagement '{request.EngagementId.Value}' not found."));

		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(engagement.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{engagement.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		// The scanner/UI picks a single opportunity to check people into (afunto#2202)
		// - without this, an organizer running two events the same day could scan a badge
		// for the other one and get a green success toast for the wrong event.
		if (engagement.OpportunityId != request.OpportunityId)
			throw new ResultFailureException(Error.NotFound(
				"Engagement.NotFound", $"No engagement matching id '{request.EngagementId.Value}' was found for this opportunity."));

		engagement.CheckIn(DateTimeOffset.UtcNow).ThrowIfFailure();

		return engagement;
	}
}
