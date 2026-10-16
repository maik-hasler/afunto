using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.CancelMeetingAsVolunteer.v1;
using Domain.Engagements;
using Domain.Primitives;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Engagements.CancelMeetingAsVolunteer.v1;

internal sealed class CancelMeetingAsVolunteerEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/me/engagements/{engagementId:guid}/meetings/{meetingId:guid}/cancel", CancelAsync)
			.WithName("CancelMeetingAsVolunteer")
			.WithTags("Engagements")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> CancelAsync(
		[FromRoute] Guid engagementId,
		[FromRoute] Guid meetingId,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var volunteerId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? uid : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var command = new CancelMeetingAsVolunteerCommand(
			EngagementId.Create(engagementId).GetValueOrThrow(),
			MeetingId.Create(meetingId).GetValueOrThrow(),
			volunteerId);

		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
