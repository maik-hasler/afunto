using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Common.Storage;

namespace Application.Users.DeleteUserAvatar.v1;

internal sealed class DeleteUserAvatarCommandHandler(
	IApplicationDbContext dbContext,
	IFileStorageService fileStorage)
	: ICommandHandler<DeleteUserAvatarCommand, bool>
{
	public async ValueTask<bool> Handle(
		DeleteUserAvatarCommand request,
		CancellationToken cancellationToken = default)
	{
		var user = await dbContext.GetOrCreateUserAsync(request.UserId, cancellationToken);

		if (user.AvatarUrl is not null)
		{
			var objectKey = fileStorage.GetObjectKeyFromPublicUrl(user.AvatarUrl);
			if (objectKey is not null)
			{
				try
				{
					await fileStorage.DeleteAsync(objectKey, cancellationToken);
				}
				catch
				{
					// Object may already be gone or storage may be transiently unavailable;
					// the local field below is what the user/UI actually observes, so still
					// clear it rather than leave a stale, now-unreachable avatar behind.
				}
			}
		}

		user.SetAvatarUrl(null);

		return true;
	}
}
