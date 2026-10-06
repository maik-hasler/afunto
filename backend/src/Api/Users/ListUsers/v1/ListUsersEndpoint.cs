using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Users.ListUsers.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.ListUsers.v1;

internal sealed class ListUsersEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/admin/users", ListUsersAsync)
			.WithName("ListUsers")
			.WithTags("Admin")
			.Produces<PagedList<AdminUserListItem>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> ListUsersAsync(
		[FromQuery] string? search,
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new ListUsersQuery(search, pageNumber, pageSize), cancellationToken);

		return Results.Ok(result);
	}
}
