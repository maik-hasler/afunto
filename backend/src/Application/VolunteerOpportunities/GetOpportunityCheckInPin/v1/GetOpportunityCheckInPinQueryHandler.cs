using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.GetOpportunityCheckInPin.v1;

internal sealed class GetOpportunityCheckInPinQueryHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IPinGenerator pinGenerator)
	: IQueryHandler<GetOpportunityCheckInPinQuery, string?>
{
	public async ValueTask<string?> Handle(
		GetOpportunityCheckInPinQuery request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(request.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		// This is the organizer's one way to learn the current PIN before announcing it at
		// the venue, so it has to rotate here too - not just on the volunteer's submission
		// path - or the screen would keep showing a previous occurrence's PIN until someone
		// happened to try checking in first (afunto#2202).
		if (opportunity.EnsureCurrentCheckInPin(DateTimeOffset.UtcNow, pinGenerator))
			await unitOfWork.SaveChangesAsync(cancellationToken);

		return opportunity.CheckInPin;
	}
}
