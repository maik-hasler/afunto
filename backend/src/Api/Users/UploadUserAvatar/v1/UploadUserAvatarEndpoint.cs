using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Storage;
using Application.Users.UploadUserAvatar.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Users.UploadUserAvatar.v1;

internal sealed class UploadUserAvatarEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/users/me/avatar", UploadUserAvatarAsync)
			.WithName("UploadUserAvatar")
			.WithTags("Users")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.DisableAntiforgery()
			.WithMetadata(new RequestSizeLimitAttribute(ImageUploadValidator.MaxRequestBodySizeBytes))
			.MapToApiVersion(1);

	private static async Task<IResult> UploadUserAvatarAsync(
		IFormFile file,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		ImageUploadValidator.EnsureValid(file.Length, file.ContentType, "Avatar");

		using var memoryStream = new MemoryStream();
		await file.CopyToAsync(memoryStream, cancellationToken);

		await sender.Send(
			new UploadUserAvatarCommand(
				userId,
				memoryStream.ToArray(),
				file.ContentType),
			cancellationToken);

		return Results.NoContent();
	}
}
