using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Geocoding;
using Application.Common.Messaging;
using Application.Maps.GeocodeAddress.v1;
using Application.VolunteerOpportunities.CreateVolunteerOpportunity.v1;
using Domain.Common;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.CreateVolunteerOpportunity.v1;

internal sealed class CreateVolunteerOpportunityEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities", CreateVolunteerOpportunityAsync)
			.WithName("CreateVolunteerOpportunity")
			.Produces<CreateVolunteerOpportunityResponse>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> CreateVolunteerOpportunityAsync(
		[FromBody] CreateVolunteerOpportunityRequest request,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

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

		if (request.CheckInPin is { Length: > 0 } pin && (pin.Length != 6 || !pin.All(char.IsAsciiDigit)))
		{
			return Results.Problem(
				"Check-in PIN must be 6 digits.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var status = request.IsDraft == true
			? OpportunityStatus.Draft
			: OpportunityStatus.Published;

		var hasAnyAddressField =
			!string.IsNullOrWhiteSpace(request.Street) ||
			!string.IsNullOrWhiteSpace(request.HouseNumber) ||
			!string.IsNullOrWhiteSpace(request.ZipCode) ||
			!string.IsNullOrWhiteSpace(request.City);

		var address = request.IsRemote || (status == OpportunityStatus.Draft && !hasAnyAddressField)
			? null
			: Address.Create(
				request.Street ?? string.Empty,
				request.HouseNumber ?? string.Empty,
				request.ZipCode ?? string.Empty,
				request.City ?? string.Empty).GetValueOrThrow();

		if (address is not null)
		{
			var geocodingResult = await sender.Send(new GeocodeAddressQuery(address), cancellationToken);

			if (geocodingResult.Outcome == GeocodingOutcome.NotFound)
			{
				// Carries an errorCode, unlike a bare Results.Problem: the client
				// keys its translated message and its jump back to the address
				// step off that code, and had nothing to act on without it (#2320).
				throw new ResultFailureException(Error.Validation(
					"Address.NotGeocodable",
					"Address could not be located. Please check the street, house number, zip code, and city."));
			}

			if (geocodingResult.Outcome == GeocodingOutcome.Found)
			{
				address = address.WithCoordinates(
					geocodingResult.Coordinates!.Latitude, geocodingResult.Coordinates.Longitude).GetValueOrThrow();
			}
		}

		var command = new CreateVolunteerOpportunityCommand(
			request.TitleDe ?? string.Empty,
			string.IsNullOrWhiteSpace(request.TitleEn) ? null : request.TitleEn,
			request.DescriptionDe ?? string.Empty,
			string.IsNullOrWhiteSpace(request.DescriptionEn) ? null : request.DescriptionEn,
			OrganizationId.Create(request.OrganizationId).GetValueOrThrow(),
			request.IsRemote,
			address,
			occurrence,
			participationType,
			checkInMethod,
			category,
			[.. request.Tags ?? []],
			status,
			userId,
			string.IsNullOrWhiteSpace(request.CheckInPin) ? null : request.CheckInPin,
			request.ValidUntil);

		var opportunity = await sender.Send(command, cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		var response = new CreateVolunteerOpportunityResponse(
			opportunity.Id.Value,
			opportunity.TitleDe,
			opportunity.TitleEn,
			opportunity.DescriptionDe,
			opportunity.DescriptionEn,
			opportunity.OrganizationId.Value,
			opportunity.Address?.Street,
			opportunity.Address?.HouseNumber,
			opportunity.Address?.ZipCode,
			opportunity.Address?.City,
			opportunity.Address?.Latitude,
			opportunity.Address?.Longitude,
			opportunity.IsRemote,
			opportunity.Occurrence.ToString(),
			opportunity.ParticipationType.ToString(),
			opportunity.CheckInMethod.ToString(),
			opportunity.Category?.ToString(),
			opportunity.Tags,
			opportunity.CreatedOn,
			opportunity.Status.ToString(),
			opportunity.ValidUntil);

		return Results.Ok(response);
	}
}
