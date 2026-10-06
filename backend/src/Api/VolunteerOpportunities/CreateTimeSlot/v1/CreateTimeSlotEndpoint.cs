using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.CreateTimeSlot.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.CreateTimeSlot.v1;

internal sealed class CreateTimeSlotEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/time-slots", CreateTimeSlotAsync)
			.WithName("CreateTimeSlot")
			.Produces<IReadOnlyList<CreateTimeSlotResponse>>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> CreateTimeSlotAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] CreateTimeSlotRequest request,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		HttpRequest httpRequest,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var recurrenceCount = request.RecurrenceCount <= 0 ? 1 : request.RecurrenceCount;
		if (recurrenceCount > 52)
			return Results.Problem("RecurrenceCount must be between 1 and 52.", statusCode: StatusCodes.Status400BadRequest);

		if (request.RecurrenceFrequency is not null &&
			!request.RecurrenceFrequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase) &&
			!request.RecurrenceFrequency.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
		{
			return Results.Problem(
				"Invalid RecurrenceFrequency. Allowed values: Weekly, Monthly.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		if (request.RecurrenceFrequency is null && recurrenceCount > 1)
		{
			return Results.Problem(
				"RecurrenceFrequency is required when RecurrenceCount is greater than 1.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var timezone = httpRequest.Headers["X-Timezone"].FirstOrDefault();
		var command = new CreateTimeSlotCommand(
			opportunityId,
			request.StartDateTime,
			request.EndDateTime,
			request.MaxParticipants,
			userId,
			request.RecurrenceFrequency,
			recurrenceCount,
			timezone);

		var timeSlots = await sender.Send(command, cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		var responses = timeSlots
			.Select(ts => new CreateTimeSlotResponse(
				ts.Id.Value, ts.StartDateTime, ts.EndDateTime, ts.MaxParticipants,
				ts.SeriesId, ts.RecurrenceFrequency, ts.RecurrenceCount))
			.ToList();

		return Results.Created($"/v1/volunteer-opportunities/{opportunityId}/time-slots", responses);
	}
}
