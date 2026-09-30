using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.SaveDashboardLayout.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.SaveDashboardLayout.v1;

internal sealed class SaveDashboardLayoutEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPut("/organizations/{organizationId:guid}/dashboard/layout", SaveDashboardLayoutAsync)
			.WithName("SaveDashboardLayout")
			.WithTags("Organizations")
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

	private static async Task<IResult> SaveDashboardLayoutAsync(
		[FromRoute] Guid organizationId,
		[FromBody] SaveDashboardLayoutRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var command = new SaveDashboardLayoutCommand(
			organizationId,
			userId,
			(request.Widgets ?? [])
				.Select(w => new DashboardWidgetPlacementInput(w.WidgetKey, w.X, w.Y, w.Width, w.Height))
				.ToList());

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
