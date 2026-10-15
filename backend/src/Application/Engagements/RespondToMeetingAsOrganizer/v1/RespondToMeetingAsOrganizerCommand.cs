using Application.Common.Messaging;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.RespondToMeetingAsOrganizer.v1;

/// <summary>
/// An organizer accepts or declines a meeting a volunteer proposed. A volunteer's
/// proposal always sits on that volunteer's one engagement.
/// </summary>
public sealed record RespondToMeetingAsOrganizerCommand(
	VolunteerOpportunityId OpportunityId,
	MeetingId MeetingId,
	bool Accept,
	UserId RequestingUserId)
	: ICommand<Engagement>;
