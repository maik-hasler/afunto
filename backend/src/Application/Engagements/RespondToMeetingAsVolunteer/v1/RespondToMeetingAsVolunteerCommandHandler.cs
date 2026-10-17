using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;

namespace Application.Engagements.RespondToMeetingAsVolunteer.v1;

internal sealed class RespondToMeetingAsVolunteerCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<RespondToMeetingAsVolunteerCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		RespondToMeetingAsVolunteerCommand request,
		CancellationToken cancellationToken = default)
	{
		var (engagement, opportunity) = await VolunteerMeetingAccess.LoadOwnAsync(
			dbContext, request.EngagementId, request.VolunteerId, cancellationToken);

		engagement.RespondToMeeting(
			request.MeetingId,
			MeetingParty.Volunteer,
			request.Accept,
			timeProvider.GetUtcNow()).ThrowIfFailure();

		await MeetingNotifier.NotifyOrganizersAsync(
			dbContext,
			opportunity.OrganizationId,
			engagement,
			request.Accept ? NotificationKind.MeetingAccepted : NotificationKind.MeetingDeclined,
			cancellationToken);

		return engagement;
	}
}
