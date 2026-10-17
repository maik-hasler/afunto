using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;

namespace Application.Engagements.CancelMeetingAsVolunteer.v1;

internal sealed class CancelMeetingAsVolunteerCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<CancelMeetingAsVolunteerCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		CancelMeetingAsVolunteerCommand request,
		CancellationToken cancellationToken = default)
	{
		var (engagement, opportunity) = await VolunteerMeetingAccess.LoadOwnAsync(
			dbContext, request.EngagementId, request.VolunteerId, cancellationToken);

		engagement.CancelMeeting(request.MeetingId, MeetingParty.Volunteer, timeProvider.GetUtcNow()).ThrowIfFailure();

		await MeetingNotifier.NotifyOrganizersAsync(
			dbContext, opportunity.OrganizationId, engagement, NotificationKind.MeetingCancelled, cancellationToken);

		return engagement;
	}
}
