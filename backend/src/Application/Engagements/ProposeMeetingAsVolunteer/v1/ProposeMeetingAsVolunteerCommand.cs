using Application.Common.Messaging;
using Domain.Engagements;

namespace Application.Engagements.ProposeMeetingAsVolunteer.v1;

/// <summary>
/// A volunteer proposes a meeting with the organization, or another time for one
/// the organization proposed. Either way it replaces the engagement's current
/// proposal; in a group meeting that takes only this volunteer out of it.
/// </summary>
public sealed record ProposeMeetingAsVolunteerCommand(
	EngagementId EngagementId,
	DateTimeOffset StartsAt,
	DateTimeOffset EndsAt,
	MeetingFormat Format,
	string? Location,
	string? Note,
	Guid VolunteerId)
	: ICommand<MeetingId>;
