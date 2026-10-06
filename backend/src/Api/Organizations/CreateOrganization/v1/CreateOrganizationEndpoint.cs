using System.Security.Claims;
using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Organizations.CreateOrganization.v1;
using Domain.Organizations;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.CreateOrganization.v1;

internal sealed class CreateOrganizationEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPost("/organizations", CreateOrganizationAsync)
			.WithName("CreateOrganization")
			.Produces<Organization>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> CreateOrganizationAsync(
		[FromBody] CreateOrganizationRequest request,
		ClaimsPrincipal user,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		if (request.Name.Length > 100)
			return Results.Problem("Name must not exceed 100 characters.", statusCode: StatusCodes.Status400BadRequest);

		var userId = Guid.Parse(user.FindFirstValue("sub")!);

		if (string.IsNullOrWhiteSpace(request.Name))
			return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);

		if (request.Name.Length > 100)
			return Results.Problem("Name must not exceed 100 characters.", statusCode: StatusCodes.Status400BadRequest);

		if (request.Description is { Length: > 1000 })
			return Results.Problem("Description must not exceed 1000 characters.", statusCode: StatusCodes.Status400BadRequest);

		var addressCommand = request.Address is null
			? null
			: new CreateAddressCommand(
				request.Address.Street,
				request.Address.HouseNumber,
				request.Address.ZipCode,
				request.Address.City);

		var command = new CreateOrganizationCommand(
			request.Name,
			userId,
			request.Description,
			request.ContactEmail,
			request.ContactPhone,
			request.Website,
			addressCommand);

		var result = await sender.Send(command, cancellationToken);

		return Results.Ok(result);
	}
}
