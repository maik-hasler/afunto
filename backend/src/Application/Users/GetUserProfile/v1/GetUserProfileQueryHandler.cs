using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;

namespace Application.Users.GetUserProfile.v1;

internal sealed class GetUserProfileQueryHandler(
	IKeycloakUserService keycloakUserService,
	IApplicationDbContext dbContext)
	: IQueryHandler<GetUserProfileQuery, MyProfileResponse>
{
	public async ValueTask<MyProfileResponse> Handle(
		GetUserProfileQuery request,
		CancellationToken cancellationToken = default)
	{
		var keycloakUser = await keycloakUserService.GetUserAsync(
			request.UserId.Value,
			cancellationToken);

		var user = await dbContext.GetOrCreateUserAsync(request.UserId, cancellationToken);

		return new MyProfileResponse(
			keycloakUser.Id,
			keycloakUser.Username,
			keycloakUser.FirstName,
			keycloakUser.LastName,
			keycloakUser.Email,
			user.AvatarUrl,
			user.Bio,
			user.Phone,
			user.Skills,
			user.Languages,
			user.PreferredContact?.ToString());
	}
}
