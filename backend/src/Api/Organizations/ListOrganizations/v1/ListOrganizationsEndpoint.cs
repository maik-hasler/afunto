using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Organizations.ListOrganizations.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.ListOrganizations.v1;

internal sealed class ListOrganizationsEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/admin/organizations", ListOrganizationsAsync)
			.WithName("ListOrganizations")
			.WithTags("Admin")
			.Produces<PagedList<AdminOrganizationSummary>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> ListOrganizationsAsync(
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromQuery] string? search,
		[FromQuery] bool? deleted,
		[FromQuery] bool? flagged,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(
			new ListOrganizationsQuery(pageNumber, pageSize, search, deleted, flagged),
			cancellationToken);

		return Results.Ok(result);
	}
}
