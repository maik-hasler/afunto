using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Storage;
using Application.Organizations.UploadOrganizationLogo.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.UploadOrganizationLogo.v1;

internal sealed class UploadOrganizationLogoEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/organizations/{organizationId:guid}/logo", UploadOrganizationLogoAsync)
			.WithName("UploadOrganizationLogo")
			.WithTags("Organizations")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.DisableAntiforgery()
			.WithMetadata(new RequestSizeLimitAttribute(ImageUploadValidator.MaxRequestBodySizeBytes))
			.MapToApiVersion(1);

	private static async Task<IResult> UploadOrganizationLogoAsync(
		[FromRoute] Guid organizationId,
		IFormFile file,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		ImageUploadValidator.EnsureValid(file.Length, file.ContentType, "Logo");

		using var memoryStream = new MemoryStream();
		await file.CopyToAsync(memoryStream, cancellationToken);

		await sender.Send(
			new UploadOrganizationLogoCommand(
				organizationId,
				memoryStream.ToArray(),
				file.ContentType,
				userId),
			cancellationToken);

		return Results.NoContent();
	}
}
