using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Primitives;

namespace Application.Engagements.CancelMeetingAsOrganizer.v1;

internal sealed class CancelMeetingAsOrganizerCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<CancelMeetingAsOrganizerCommand, int>
{
	public async ValueTask<int> Handle(
		CancelMeetingAsOrganizerCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(request.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		// Only active engagements can carry a meeting: withdrawing, cancelling and
		// anonymizing an engagement all drop it.
		var engagements = (await dbContext.GetActiveEngagementsForOpportunityAsync(request.OpportunityId, cancellationToken))
			.Where(e => e.Meeting?.Id == request.MeetingId)
			.ToList();

		if (engagements.Count == 0)
			throw new ResultFailureException(Error.Conflict("Meeting.Superseded", "This meeting proposal is no longer current."));

		var now = timeProvider.GetUtcNow();
		foreach (var engagement in engagements)
		{
			engagement.CancelMeeting(request.MeetingId, MeetingParty.Organization, now).ThrowIfFailure();
			await MeetingNotifier.NotifyVolunteerAsync(dbContext, engagement, NotificationKind.MeetingCancelled, cancellationToken);
		}

		return engagements.Count;
	}
}
