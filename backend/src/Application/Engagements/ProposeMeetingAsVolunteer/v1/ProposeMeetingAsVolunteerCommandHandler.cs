using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.ProposeMeetingAsVolunteer.v1;

internal sealed class ProposeMeetingAsVolunteerCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<ProposeMeetingAsVolunteerCommand, MeetingId>
{
	public async ValueTask<MeetingId> Handle(
		ProposeMeetingAsVolunteerCommand request,
		CancellationToken cancellationToken = default)
	{
		var (engagement, opportunity) = await VolunteerMeetingAccess.LoadOwnAsync(
			dbContext, request.EngagementId, request.VolunteerId, cancellationToken);

		if (opportunity.ParticipationType != ParticipationType.IndividualContact)
			throw new ResultFailureException(Error.Validation("Meeting.RequiresIndividualContact", "Meetings can only be arranged for an expression of interest."));

		var meetingId = MeetingId.New();
		engagement.ProposeMeeting(
			meetingId,
			MeetingParty.Volunteer,
			request.StartsAt,
			request.EndsAt,
			request.Location,
			request.Note,
			timeProvider.GetUtcNow()).ThrowIfFailure();

		await MeetingNotifier.NotifyOrganizersAsync(
			dbContext, opportunity.OrganizationId, engagement, NotificationKind.MeetingProposed, cancellationToken);

		return meetingId;
	}
}
