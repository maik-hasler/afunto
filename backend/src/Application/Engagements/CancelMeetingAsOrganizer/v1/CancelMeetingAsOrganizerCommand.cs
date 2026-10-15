using Application.Common.Messaging;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.CancelMeetingAsOrganizer.v1;

/// <summary>
/// Cancels a meeting the organization proposed, for everyone it was proposed to.
/// Returns how many engagements still carried it.
/// </summary>
public sealed record CancelMeetingAsOrganizerCommand(
	VolunteerOpportunityId OpportunityId,
	MeetingId MeetingId,
	UserId RequestingUserId)
	: ICommand<int>;
