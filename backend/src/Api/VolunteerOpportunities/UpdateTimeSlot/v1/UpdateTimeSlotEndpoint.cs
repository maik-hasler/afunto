using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.UpdateTimeSlot.v1;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.UpdateTimeSlot.v1;

internal sealed class UpdateTimeSlotEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/volunteer-opportunities/{opportunityId:guid}/time-slots/{timeSlotId:guid}", UpdateTimeSlotAsync)
			.WithName("UpdateTimeSlot")
			.Produces<UpdateTimeSlotResponse>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> UpdateTimeSlotAsync(
		[FromRoute] Guid opportunityId,
		[FromRoute] Guid timeSlotId,
		[FromBody] UpdateTimeSlotRequest request,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var scope = SeriesEditScope.Only;
		if (!string.IsNullOrEmpty(request.Scope) && !Enum.TryParse(request.Scope, ignoreCase: true, out scope))
		{
			return Results.Problem(
				"Invalid Scope. Allowed values: Only, ThisAndFollowing, EntireSeries.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlotId, request.StartDateTime, request.EndDateTime, request.MaxParticipants, userId, scope);
		var result = await sender.Send(command, cancellationToken);
		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);
		return Results.Ok(new UpdateTimeSlotResponse(result.UpdatedCount, result.SkippedTimeSlotIds));
	}
}
