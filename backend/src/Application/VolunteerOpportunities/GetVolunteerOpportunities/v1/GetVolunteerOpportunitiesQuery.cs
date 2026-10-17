using Application.Common.Messaging;
using Application.Common.Pagination;

namespace Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;

public sealed record GetVolunteerOpportunitiesQuery(
	int PageNumber,
	int PageSize,
	string? Occurrence,
	string? ParticipationType,
	bool? IsRemote,
	DateTimeOffset? DateFrom,
	DateTimeOffset? DateTo,
	double? CenterLatitude,
	double? CenterLongitude,
	double? RadiusKm,
	string[]? Categories,
	string? Tag,
	string? Keyword,
	Guid? RequestingUserId = null)
	: IQuery<PagedList<VolunteerOpportunitySummary>>;
