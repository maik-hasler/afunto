using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Common.Storage;

namespace Application.Users.UploadUserAvatar.v1;

internal sealed class UploadUserAvatarCommandHandler(
	IApplicationDbContext dbContext,
	IFileStorageService fileStorage)
	: ICommandHandler<UploadUserAvatarCommand, bool>
{
	public async ValueTask<bool> Handle(
		UploadUserAvatarCommand request,
		CancellationToken cancellationToken = default)
	{
		var contentType = ImageUploadValidator.EnsureValid(request.Content, request.ContentType, "Avatar");

		var user = await dbContext.GetOrCreateUserAsync(request.UserId, cancellationToken);
		var previousAvatarUrl = user.AvatarUrl;

		var ext = ImageUploadValidator.GetExtension(contentType);
		// Random suffix, not just "{userId}{ext}" - the user id is a public
		// identifier surfaced by member search and public-profile endpoints, and
		// avatars are face photos, so the object key must not be guessable from it
		// (issue #1175).
		var objectKey = $"user-avatars/{request.UserId.Value}/{Guid.NewGuid():N}{ext}";

		using var stream = new MemoryStream(request.Content);
		var url = await fileStorage.UploadAsync(objectKey, stream, request.Content.Length, contentType, cancellationToken);

		user.SetAvatarUrl(url);

		await DeletePreviousAvatarAsync(previousAvatarUrl, cancellationToken);

		return true;
	}

	private async Task DeletePreviousAvatarAsync(string? previousAvatarUrl, CancellationToken cancellationToken)
	{
		if (previousAvatarUrl is null)
			return;

		var previousObjectKey = fileStorage.GetObjectKeyFromPublicUrl(previousAvatarUrl);
		if (previousObjectKey is null)
			return;

		try
		{
			await fileStorage.DeleteAsync(previousObjectKey, cancellationToken);
		}
		catch
		{
			// Object may already be gone or storage may be transiently unavailable; continue.
		}
	}
}
