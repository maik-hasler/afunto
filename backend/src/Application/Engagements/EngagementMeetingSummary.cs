namespace Application.Engagements;

/// <summary>
/// The engagement's current meeting proposal. ParticipantCount is how many active
/// engagements share it: above one it is a group meeting. Names of the other
/// participants are deliberately not part of a volunteer's view.
/// </summary>
public sealed record EngagementMeetingSummary(
	Guid Id,
	DateTimeOffset StartsAt,
	DateTimeOffset EndsAt,
	string Location,
	string? Note,
	string ProposedBy,
	string Response,
	int ParticipantCount);
