using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;

namespace Application.Users.UpdateUserProfile.v1;

internal sealed class UpdateUserProfileCommandHandler(
	IKeycloakUserService keycloakUserService,
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork)
	: ICommandHandler<UpdateUserProfileCommand, bool>
{
	public async ValueTask<bool> Handle(
		UpdateUserProfileCommand request,
		CancellationToken cancellationToken = default)
	{
		await keycloakUserService.UpdateUserAsync(
			request.UserId.Value,
			request.FirstName,
			request.LastName,
			cancellationToken);

		var user = await dbContext.GetOrCreateUserAsync(request.UserId, cancellationToken);

		user.ChangeBio(request.Bio);
		user.SetPhone(request.Phone);
		user.UpdateSkills(request.Skills);
		user.UpdateLanguages(request.Languages);
		user.SetPreferredContact(request.PreferredContactValue);

		await unitOfWork.SaveChangesAsync(cancellationToken);

		return true;
	}
}
