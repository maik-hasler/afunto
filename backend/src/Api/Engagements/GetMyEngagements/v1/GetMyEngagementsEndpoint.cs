using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Engagements;
using Application.Engagements.GetMyEngagements.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Engagements.GetMyEngagements.v1;

internal sealed class GetMyEngagementsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/me/engagements", GetMyEngagementsAsync)
			.WithName("GetMyEngagements")
			.WithTags("Engagements")
			.Produces<PagedList<EngagementSummary>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetMyEngagementsAsync(
		[AsParameters] GetMyEngagementsRequest request,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		if (request.PageNumber < 1)
			return Results.Problem("PageNumber must be at least 1.", statusCode: StatusCodes.Status400BadRequest);

		if (request.PageSize < 1 || request.PageSize > 100)
			return Results.Problem("PageSize must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
		{
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);
		}

		var query = new GetMyEngagementsQuery(
			UserId.Create(userId).GetValueOrThrow(),
			request.PageNumber,
			request.PageSize,
			request.Upcoming);
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
