using Application.Common.Messaging;
using Application.Common.Pagination;

namespace Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;

internal sealed class GetVolunteerOpportunitiesQueryHandler(
	IVolunteerOpportunityReadRepository readRepository)
	: IQueryHandler<GetVolunteerOpportunitiesQuery, PagedList<VolunteerOpportunitySummary>>
{
	private const int MaxPageSize = 100;

	public async ValueTask<PagedList<VolunteerOpportunitySummary>> Handle(
		GetVolunteerOpportunitiesQuery request,
		CancellationToken cancellationToken = default)
	{
		var pageNumber = Math.Max(1, request.PageNumber);
		var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

		var filter = new VolunteerOpportunityFilter(
			pageNumber,
			pageSize,
			request.Occurrence,
			request.ParticipationType,
			request.IsRemote,
			request.DateFrom,
			request.DateTo,
			request.CenterLatitude,
			request.CenterLongitude,
			request.RadiusKm,
			request.Categories,
			request.Tag,
			request.Keyword,
			request.RequestingUserId);

		return await readRepository.GetPagedSummariesAsync(filter, cancellationToken);
	}
}
