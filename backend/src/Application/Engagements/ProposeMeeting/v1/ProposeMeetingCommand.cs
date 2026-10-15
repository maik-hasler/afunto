using Application.Common.Messaging;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.ProposeMeeting.v1;

/// <summary>
/// An organizer proposes one meeting to one or several people who expressed
/// interest. Several engagements make it a group meeting: all of them carry the same
/// proposal, and each person answers for themselves. ReplacesMeetingId moves a
/// meeting: everyone still carrying that proposal gets the new one, without the
/// caller having to know who that is.
/// </summary>
public sealed record ProposeMeetingCommand(
	VolunteerOpportunityId OpportunityId,
	IReadOnlyList<EngagementId> EngagementIds,
	DateTimeOffset StartsAt,
	DateTimeOffset EndsAt,
	string? Location,
	string? Note,
	UserId RequestingUserId,
	MeetingId? ReplacesMeetingId = null)
	: ICommand<MeetingId>;
