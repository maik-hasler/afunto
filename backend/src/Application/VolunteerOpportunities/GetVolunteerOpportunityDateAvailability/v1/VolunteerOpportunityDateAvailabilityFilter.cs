namespace Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;

public sealed record VolunteerOpportunityDateAvailabilityFilter(
	DateTimeOffset From,
	DateTimeOffset To,
	string? Timezone,
	string? Occurrence = null,
	string? ParticipationType = null,
	bool? IsRemote = null,
	double? CenterLatitude = null,
	double? CenterLongitude = null,
	double? RadiusKm = null,
	string[]? Categories = null,
	string? Tag = null,
	string? Keyword = null,
	Guid? RequestingUserId = null)
{
	public bool HasRadius => CenterLatitude.HasValue && CenterLongitude.HasValue && RadiusKm is > 0;
}
