using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Users.UpdateUserProfile.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.UpdateUserProfile.v1;

internal sealed class UpdateUserProfileEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/users/me", UpdateUserProfileAsync)
			.WithName("UpdateUserProfile")
			.WithTags("Users")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> UpdateUserProfileAsync(
		[FromBody] UpdateUserProfileRequest request,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
		{
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);
		}

		if (request.Skills is { Count: > UpdateUserProfileRequest.MaxSkillsCount })
			return Results.Problem($"Skills must not contain more than {UpdateUserProfileRequest.MaxSkillsCount} entries.", statusCode: StatusCodes.Status400BadRequest);

		if (request.Skills?.Any(skill => skill.Length > UpdateUserProfileRequest.MaxSkillLength) is true)
			return Results.Problem($"Each skill must not exceed {UpdateUserProfileRequest.MaxSkillLength} characters.", statusCode: StatusCodes.Status400BadRequest);

		if (request.Languages is { Count: > UpdateUserProfileRequest.MaxLanguagesCount })
			return Results.Problem($"Languages must not contain more than {UpdateUserProfileRequest.MaxLanguagesCount} entries.", statusCode: StatusCodes.Status400BadRequest);

		if (request.Languages?.Any(language => language.Length > UpdateUserProfileRequest.MaxLanguageLength) is true)
			return Results.Problem($"Each language must not exceed {UpdateUserProfileRequest.MaxLanguageLength} characters.", statusCode: StatusCodes.Status400BadRequest);

		Domain.Users.PreferredContact? preferredContact = null;
		if (request.PreferredContact is not null &&
			Enum.TryParse<Domain.Users.PreferredContact>(request.PreferredContact, out var parsed))
		{
			preferredContact = parsed;
		}

		var command = new UpdateUserProfileCommand(
			UserId.Create(userId).GetValueOrThrow(),
			request.FirstName,
			request.LastName,
			request.Bio,
			request.Phone,
			request.Skills ?? [],
			request.Languages ?? [],
			preferredContact,
			SupportedLanguages.Resolve(request.PreferredLanguage));

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
