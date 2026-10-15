using Application.Common.Messaging;
using Application.Users.GetPermissionsChangedOn.v1;
using Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Common.Authentication;

/// <summary>
/// Rejects an access token issued before the user's permissions last changed.
///
/// Keycloak bakes roles into the token, so a demoted admin, a blocked user or a
/// freshly promoted organizer would otherwise keep their old permissions until
/// the token expires (#2395, #2403, #2407). A 401 makes the client fetch a new
/// token, which carries the current roles - or fails, for a blocked user.
/// </summary>
internal static class PermissionsChangedTokenValidator
{
	public static async Task ValidateAsync(TokenValidatedContext context)
	{
		// No user behind the token (e.g. a client-credentials call): nothing to check.
		if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var subject))
			return;

		var userId = UserId.Create(subject);
		if (userId.IsFailure)
			return;

		var sender = context.HttpContext.RequestServices.GetRequiredService<ISender>();
		var permissionsChangedOn = await sender.Send(
			new GetPermissionsChangedOnQuery(userId.Value),
			context.HttpContext.RequestAborted);

		if (permissionsChangedOn is null)
			return;

		if (IsIssuedBefore(context.SecurityToken as JsonWebToken, permissionsChangedOn.Value))
			context.Fail("The access token was issued before the user's permissions changed.");
	}

	// `iat` has whole-second precision, so a token stamped in the same second as
	// the change may still carry the old permissions - it counts as "before".
	// A token without `iat` cannot prove it is newer and is rejected too.
	internal static bool IsIssuedBefore(JsonWebToken? token, DateTimeOffset permissionsChangedOn)
	{
		if (token is null || token.IssuedAt == DateTime.MinValue)
			return true;

		var changedOnWholeSecond = DateTimeOffset.FromUnixTimeSeconds(permissionsChangedOn.ToUnixTimeSeconds());

		return new DateTimeOffset(token.IssuedAt, TimeSpan.Zero) <= changedOnWholeSecond;
	}
}
