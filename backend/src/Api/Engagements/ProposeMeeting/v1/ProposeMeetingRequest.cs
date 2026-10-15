using System.ComponentModel.DataAnnotations;

namespace Api.Engagements.ProposeMeeting.v1;

public sealed record ProposeMeetingRequest(
	IReadOnlyList<Guid> EngagementIds,
	DateTimeOffset StartsAt,
	DateTimeOffset EndsAt,
	[MaxLength(20)] string Format,
	[MaxLength(300)] string Location,
	[MaxLength(500)] string? Note,
	Guid? ReplacesMeetingId = null);
