using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.GetOrganizationOpportunities.v1;

public sealed record GetOrganizationOpportunitiesQuery(
	Guid OrganizationId,
	UserId RequestingUserId,
	OpportunityStatus Status,
	int PageNumber,
	int PageSize,
	bool? MembersOnly = null)
	: IQuery<PagedList<VolunteerOpportunitySummary>>;
