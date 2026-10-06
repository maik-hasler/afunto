namespace Application.Common.Storage;

public interface IFileStorageService
{
	Task<string> UploadAsync(
		string objectKey,
		Stream content,
		long size,
		string contentType,
		CancellationToken cancellationToken = default);

	Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

	// Moves the object out of the publicly-readable prefix instead of deleting
	// it, so a moderation reversal (UnquarantineAsync) can move it back - see
	// afunto#2198.
	Task QuarantineAsync(string objectKey, CancellationToken cancellationToken = default);

	Task UnquarantineAsync(string objectKey, CancellationToken cancellationToken = default);

	string? GetObjectKeyFromPublicUrl(string publicUrl);

	Task PingAsync(CancellationToken cancellationToken = default);
}
