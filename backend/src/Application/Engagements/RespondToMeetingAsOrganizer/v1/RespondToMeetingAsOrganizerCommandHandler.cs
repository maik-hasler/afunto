using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Primitives;

namespace Application.Engagements.RespondToMeetingAsOrganizer.v1;

internal sealed class RespondToMeetingAsOrganizerCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<RespondToMeetingAsOrganizerCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		RespondToMeetingAsOrganizerCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(request.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		var engagement = (await dbContext.GetActiveEngagementsForOpportunityAsync(request.OpportunityId, cancellationToken))
			.FirstOrDefault(e => e.Meeting?.Id == request.MeetingId)
			?? throw new ResultFailureException(Error.Conflict("Meeting.Superseded", "This meeting proposal is no longer current."));

		engagement.RespondToMeeting(
			request.MeetingId,
			MeetingParty.Organization,
			request.Accept,
			timeProvider.GetUtcNow()).ThrowIfFailure();

		await MeetingNotifier.NotifyVolunteerAsync(
			dbContext,
			engagement,
			request.Accept ? NotificationKind.MeetingAccepted : NotificationKind.MeetingDeclined,
			cancellationToken);

		return engagement;
	}
}
