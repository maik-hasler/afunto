using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.AssignMemberToOpportunity.v1;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.Engagements.AssignMemberToOpportunity.v1;

internal sealed class AssignMemberToOpportunityEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/assignments", AssignMemberToOpportunityAsync)
			.WithName("AssignMemberToOpportunity")
			.WithTags("Engagements")
			.Produces<EngagementStatusResponse>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> AssignMemberToOpportunityAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] AssignMemberToOpportunityRequest request,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var requestingUserId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (request.Message is { Length: > 500 })
			return Results.Problem("Message must not exceed 500 characters.", statusCode: StatusCodes.Status400BadRequest);

		var command = new AssignMemberToOpportunityCommand(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(),
			UserId.Create(request.MemberId).GetValueOrThrow(),
			request.TimeSlotId.HasValue ? TimeSlotId.Create(request.TimeSlotId.Value).GetValueOrThrow() : null,
			request.Message,
			requestingUserId);

		var engagement = await sender.Send(command, cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.Created(
			$"/v1/engagements/{engagement.Id.Value}",
			new EngagementStatusResponse(engagement.Id.Value, engagement.Status.ToString()));
	}
}
