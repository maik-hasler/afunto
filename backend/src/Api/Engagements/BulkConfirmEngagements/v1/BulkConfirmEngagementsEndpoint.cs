using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.BulkConfirmEngagements.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Engagements.BulkConfirmEngagements.v1;

internal sealed class BulkConfirmEngagementsEndpoint
	: IEndpoint
{
	private const int MaxBatchSize = 200;

	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/engagements/bulk-confirm", BulkConfirmEngagementsAsync)
			.WithName("BulkConfirmEngagements")
			.WithTags("Engagements")
			.Produces<BulkEngagementActionResponse>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> BulkConfirmEngagementsAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] BulkConfirmEngagementsRequest? body,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (body is null || body.EngagementIds.Count == 0)
			return Results.Problem("At least one engagement id is required.", statusCode: StatusCodes.Status400BadRequest);

		if (body.EngagementIds.Count > MaxBatchSize)
			return Results.Problem($"Cannot process more than {MaxBatchSize} engagements in a single request.", statusCode: StatusCodes.Status400BadRequest);

		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var command = new BulkConfirmEngagementsCommand(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(),
			body.EngagementIds.Select(id => EngagementId.Create(id).GetValueOrThrow()).ToList(),
			userId);

		var result = await sender.Send(command, cancellationToken);

		return Results.Ok(new BulkEngagementActionResponse(
			result.Succeeded.Select(s => new EngagementStatusResponse(s.EngagementId, s.Status, s.CancellationReason)).ToList(),
			result.Failed.Select(f => new BulkEngagementActionFailureResponse(f.EngagementId, f.ErrorCode, f.Message)).ToList()));
	}
}
