using System.ComponentModel.DataAnnotations;

namespace Api.Engagements.ProposeMeetingAsVolunteer.v1;

public sealed record ProposeMeetingAsVolunteerRequest(
	DateTimeOffset StartsAt,
	DateTimeOffset EndsAt,
	[MaxLength(20)] string Format,
	[MaxLength(300)] string Location,
	[MaxLength(500)] string? Note);
