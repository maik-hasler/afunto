using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.UpdateOrganization.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.UpdateOrganization.v1;

internal sealed class UpdateOrganizationEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPut("/organizations/{organizationId:guid}", UpdateOrganizationAsync)
			.WithName("UpdateOrganization")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> UpdateOrganizationAsync(
		[FromRoute] Guid organizationId,
		[FromBody] UpdateOrganizationRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (string.IsNullOrWhiteSpace(request.Name))
			return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);

		var addressCommand = request.Address is null
			? null
			: new UpdateAddressCommand(
				request.Address.Street,
				request.Address.HouseNumber,
				request.Address.ZipCode,
				request.Address.City);

		var command = new UpdateOrganizationCommand(
			organizationId,
			request.Name,
			request.Description,
			request.ContactEmail,
			request.ContactPhone,
			request.Website,
			addressCommand,
			userId);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
