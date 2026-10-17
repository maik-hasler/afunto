using System.ComponentModel.DataAnnotations;

namespace Api.VolunteerOpportunities.CreateVolunteerOpportunity.v1;

public sealed record CreateVolunteerOpportunityRequest(
	[MaxLength(200)] string? TitleDe,
	[MaxLength(200)] string? TitleEn,
	[MaxLength(5000)] string? DescriptionDe,
	[MaxLength(5000)] string? DescriptionEn,
	Guid OrganizationId,
	bool IsRemote,
	[MaxLength(200)] string? Street,
	[MaxLength(20)] string? HouseNumber,
	[MaxLength(10)] string? ZipCode,
	[MaxLength(100)] string? City,
	string Occurrence,
	string ParticipationType,
	string CheckInMethod,
	string? Category,
	IReadOnlyList<string>? Tags,
	bool? IsDraft,
	[MaxLength(6)] string? CheckInPin,
	DateTimeOffset? ValidUntil,
	bool MembersOnly = false);
