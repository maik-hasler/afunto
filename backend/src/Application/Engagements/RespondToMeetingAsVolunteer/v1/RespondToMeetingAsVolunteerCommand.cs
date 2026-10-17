using Application.Common.Messaging;
using Domain.Engagements;

namespace Application.Engagements.RespondToMeetingAsVolunteer.v1;

public sealed record RespondToMeetingAsVolunteerCommand(
	EngagementId EngagementId,
	MeetingId MeetingId,
	bool Accept,
	Guid VolunteerId)
	: ICommand<Engagement>;
