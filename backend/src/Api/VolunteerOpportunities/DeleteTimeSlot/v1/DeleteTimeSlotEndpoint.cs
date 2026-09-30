using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.DeleteTimeSlot.v1;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.DeleteTimeSlot.v1;

internal sealed class DeleteTimeSlotEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/volunteer-opportunities/{opportunityId:guid}/time-slots/{timeSlotId:guid}", DeleteTimeSlotAsync)
			.WithName("DeleteTimeSlot")
			.Produces<DeleteTimeSlotResponse>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteTimeSlotAsync(
		[FromRoute] Guid opportunityId,
		[FromRoute] Guid timeSlotId,
		[FromQuery] string? scope,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var seriesScope = SeriesEditScope.Only;
		if (!string.IsNullOrEmpty(scope) && !Enum.TryParse(scope, ignoreCase: true, out seriesScope))
		{
			return Results.Problem(
				"Invalid scope. Allowed values: Only, ThisAndFollowing, EntireSeries.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var command = new DeleteTimeSlotCommand(opportunityId, timeSlotId, userId, seriesScope);
		var result = await sender.Send(command, cancellationToken);
		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);
		return Results.Ok(new DeleteTimeSlotResponse(result.DeletedTimeSlotIds));
	}
}
