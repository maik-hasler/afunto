using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Reports.ListFlaggedTargets.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Reports.ListFlaggedTargets.v1;

internal sealed class ListFlaggedTargetsEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/admin/reports/targets", ListFlaggedTargetsAsync)
			.WithName("ListFlaggedTargets")
			.WithTags("Admin")
			.Produces<PagedList<FlaggedTargetSummary>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> ListFlaggedTargetsAsync(
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromQuery] bool? includeResolved,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(
			new ListFlaggedTargetsQuery(pageNumber, pageSize, includeResolved ?? false),
			cancellationToken);

		return Results.Ok(result);
	}
}
