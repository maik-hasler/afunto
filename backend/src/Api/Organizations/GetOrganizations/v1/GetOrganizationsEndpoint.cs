using System.Security.Claims;
using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Organizations.GetOrganizations.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.GetOrganizations.v1;

internal sealed class GetOrganizationsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapGet("/organizations", GetOrganizationsAsync)
			.WithName("GetOrganizations")
			.Produces<IReadOnlyList<OrganizationSummaryDto>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> GetOrganizationsAsync(
		ClaimsPrincipal user,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		var userId = Guid.Parse(user.FindFirstValue("sub")!);

		var query = new GetOrganizationsQuery(userId);

		var result = await sender.Send(query, cancellationToken);

		return Results.Ok(result);
	}
}
