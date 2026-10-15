using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.ProposeMeeting.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Engagements.ProposeMeeting.v1;

internal sealed class ProposeMeetingEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/meetings", ProposeMeetingAsync)
			.WithName("ProposeMeeting")
			.WithTags("Engagements")
			.Produces<MeetingProposedResponse>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> ProposeMeetingAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] ProposeMeetingRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var command = new ProposeMeetingCommand(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(),
			(request.EngagementIds ?? []).Select(id => EngagementId.Create(id).GetValueOrThrow()).ToList(),
			request.StartsAt,
			request.EndsAt,
			request.Location,
			request.Note,
			userId,
			request.ReplacesMeetingId is Guid replaced ? MeetingId.Create(replaced).GetValueOrThrow() : null);

		var meetingId = await sender.Send(command, cancellationToken);
		return Results.Ok(new MeetingProposedResponse(meetingId.Value));
	}
}
