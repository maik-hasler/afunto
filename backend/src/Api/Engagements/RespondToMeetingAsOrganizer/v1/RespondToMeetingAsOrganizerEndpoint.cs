using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.RespondToMeetingAsOrganizer.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Engagements.RespondToMeetingAsOrganizer.v1;

internal sealed class RespondToMeetingAsOrganizerEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/meetings/{meetingId:guid}/respond", RespondAsync)
			.WithName("RespondToMeetingAsOrganizer")
			.WithTags("Engagements")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> RespondAsync(
		[FromRoute] Guid opportunityId,
		[FromRoute] Guid meetingId,
		[FromBody] RespondToMeetingRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var command = new RespondToMeetingAsOrganizerCommand(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(),
			MeetingId.Create(meetingId).GetValueOrThrow(),
			request.Accept,
			userId);

		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
