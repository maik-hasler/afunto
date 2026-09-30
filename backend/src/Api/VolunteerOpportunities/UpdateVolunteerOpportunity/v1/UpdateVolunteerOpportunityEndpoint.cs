using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.UpdateVolunteerOpportunity.v1;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;
using Domain.Common;

namespace Api.VolunteerOpportunities.UpdateVolunteerOpportunity.v1;

internal sealed class UpdateVolunteerOpportunityEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/volunteer-opportunities/{opportunityId:guid}", UpdateVolunteerOpportunityAsync)
			.WithName("UpdateVolunteerOpportunity")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> UpdateVolunteerOpportunityAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] UpdateVolunteerOpportunityRequest request,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (request.CheckInPin is { Length: > 0 } pin && (pin.Length != 6 || !pin.All(char.IsAsciiDigit)))
		{
			return Results.Problem(
				"Check-in PIN must be 6 digits.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		Address? address = null;
		if (!request.IsRemote && !string.IsNullOrWhiteSpace(request.Street))
			address = Address.Create(
				request.Street ?? string.Empty,
				request.HouseNumber ?? string.Empty,
				request.ZipCode ?? string.Empty,
				request.City ?? string.Empty).GetValueOrThrow();

		if (!Enum.TryParse<Occurrence>(request.Occurrence, ignoreCase: true, out var occurrence) || !Enum.IsDefined(occurrence))
		{
			return Results.Problem(
				"Invalid occurrence. Allowed values: OneTime, Recurring.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		if (!Enum.TryParse<ParticipationType>(request.ParticipationType, ignoreCase: true, out var participationType) || !Enum.IsDefined(participationType))
		{
			return Results.Problem(
				"Invalid participation type. Allowed values: ScheduledSlots, IndividualContact.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		if (!Enum.TryParse<CheckInMethod>(request.CheckInMethod, ignoreCase: true, out var checkInMethod) || !Enum.IsDefined(checkInMethod))
		{
			return Results.Problem(
				"Invalid check-in method. Allowed values: None, QRCode, PINCode, Manual.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		Category? category = null;
		if (!string.IsNullOrWhiteSpace(request.Category))
		{
			if (!Enum.TryParse<Category>(request.Category, ignoreCase: true, out var parsedCategory) || !Enum.IsDefined(parsedCategory))
			{
				return Results.Problem(
					"Invalid category.",
					statusCode: StatusCodes.Status400BadRequest);
			}
			category = parsedCategory;
		}

		var command = new UpdateVolunteerOpportunityCommand(
			opportunityId,
			request.TitleDe ?? string.Empty,
			string.IsNullOrWhiteSpace(request.TitleEn) ? null : request.TitleEn,
			request.DescriptionDe ?? string.Empty,
			string.IsNullOrWhiteSpace(request.DescriptionEn) ? null : request.DescriptionEn,
			request.IsRemote,
			address,
			occurrence,
			participationType,
			checkInMethod,
			category,
			[.. request.Tags ?? []],
			userId,
			string.IsNullOrWhiteSpace(request.CheckInPin) ? null : request.CheckInPin,
			request.ValidUntil);

		await sender.Send(command, cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.NoContent();
	}
}
