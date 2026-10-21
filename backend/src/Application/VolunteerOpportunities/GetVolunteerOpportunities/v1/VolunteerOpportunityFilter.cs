namespace Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;

public sealed record VolunteerOpportunityFilter(
	int PageNumber,
	int PageSize,
	string? Occurrence = null,
	string? ParticipationType = null,
	bool? IsRemote = null,
	DateTimeOffset? DateFrom = null,
	DateTimeOffset? DateTo = null,
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
