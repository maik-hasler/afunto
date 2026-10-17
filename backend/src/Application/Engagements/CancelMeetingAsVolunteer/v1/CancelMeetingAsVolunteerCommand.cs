using Application.Common.Messaging;
using Domain.Engagements;

namespace Application.Engagements.CancelMeetingAsVolunteer.v1;

public sealed record CancelMeetingAsVolunteerCommand(
	EngagementId EngagementId,
	MeetingId MeetingId,
	Guid VolunteerId)
	: ICommand<Engagement>;
