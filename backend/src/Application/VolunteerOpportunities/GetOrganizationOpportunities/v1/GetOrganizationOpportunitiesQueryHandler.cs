using Application.Common.Authorization;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Common.Persistence;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;

namespace Application.VolunteerOpportunities.GetOrganizationOpportunities.v1;

internal sealed class GetOrganizationOpportunitiesQueryHandler(
	IVolunteerOpportunityReadRepository readRepository,
	IApplicationDbContext dbContext)
	: IQueryHandler<GetOrganizationOpportunitiesQuery, PagedList<VolunteerOpportunitySummary>>
{
	private const int MaxPageSize = 100;

	public async ValueTask<PagedList<VolunteerOpportunitySummary>> Handle(
		GetOrganizationOpportunitiesQuery request,
		CancellationToken cancellationToken = default)
	{
		await OwnershipGuard.EnsureIsMemberAsync(
			dbContext,
			request.OrganizationId,
			request.RequestingUserId,
			cancellationToken);

		var pageNumber = Math.Max(1, request.PageNumber);
		var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

		return await readRepository.GetPagedSummariesByOrganizationAsync(
			request.OrganizationId,
			request.Status,
			pageNumber,
			pageSize,
			request.MembersOnly,
			cancellationToken);
	}
}
