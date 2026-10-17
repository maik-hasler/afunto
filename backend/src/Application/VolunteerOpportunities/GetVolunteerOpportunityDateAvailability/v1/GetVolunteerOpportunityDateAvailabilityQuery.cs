using Application.Common.Messaging;

namespace Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;

public sealed record GetVolunteerOpportunityDateAvailabilityQuery(
	DateTimeOffset From,
	DateTimeOffset To,
	string? Timezone,
	string? Occurrence,
	string? ParticipationType,
	bool? IsRemote,
	double? CenterLatitude,
	double? CenterLongitude,
	double? RadiusKm,
	string[]? Categories,
	string? Tag,
	string? Keyword,
	Guid? RequestingUserId = null)
	: IQuery<IReadOnlyList<VolunteerOpportunityAvailableDate>>;
