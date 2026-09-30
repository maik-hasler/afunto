using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Storage;
using Application.VolunteerOpportunities.UploadOpportunityBanner.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.UploadOpportunityBanner.v1;

internal sealed class UploadOpportunityBannerEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/volunteer-opportunities/{opportunityId:guid}/banner", UploadOpportunityBannerAsync)
			.WithName("UploadOpportunityBanner")
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

	private static async Task<IResult> UploadOpportunityBannerAsync(
		[FromRoute] Guid opportunityId,
		IFormFile file,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		ImageUploadValidator.EnsureValid(file.Length, file.ContentType, "Banner");

		using var memoryStream = new MemoryStream();
		await file.CopyToAsync(memoryStream, cancellationToken);

		await sender.Send(
			new UploadOpportunityBannerCommand(
				opportunityId,
				memoryStream.ToArray(),
				file.ContentType,
				userId),
			cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.NoContent();
	}
}
