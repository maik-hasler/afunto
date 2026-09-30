using Application.Common.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Infrastructure.Storage;

internal sealed class MinioFileStorageService : IFileStorageService
{
	internal const string CacheControlHeaderValue = "public, max-age=3600";

	private const string PublicPrefix = "public/";

	// Not covered by the bucket policy set up in EnsureBucketReadyAsync (which
	// only grants anonymous reads under PublicPrefix), so moving an object here
	// makes it unreachable by its old public URL without discarding it - see
	// afunto#2198.
	private const string QuarantinePrefix = "quarantined/";

	private readonly IMinioClient _minio;
	private readonly StorageSettings _settings;
	private static readonly SemaphoreSlim _initLock = new(1, 1);
	private static bool _bucketReady;

	public MinioFileStorageService(IOptions<StorageSettings> settings)
	{
		_settings = settings.Value;

		var endpoint = _settings.Endpoint.TrimEnd('/');
		if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
		{
			_minio = new MinioClient()
				.WithEndpoint(uri.Host, uri.Port)
				.WithCredentials(_settings.AccessKey, _settings.SecretKey)
				.WithSSL(uri.Scheme == "https")
				.Build();
		}
		else
		{
			_minio = new MinioClient()
				.WithEndpoint(endpoint)
				.WithCredentials(_settings.AccessKey, _settings.SecretKey)
				.Build();
		}
	}

	public async Task<string> UploadAsync(
		string objectKey,
		Stream content,
		long size,
		string contentType,
		CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		await _minio.PutObjectAsync(
			new PutObjectArgs()
				.WithBucket(_settings.BucketName)
				.WithObject(PublicPrefix + objectKey)
				.WithStreamData(content)
				.WithObjectSize(size)
				.WithContentType(contentType)
				.WithHeaders(new Dictionary<string, string>
				{
					["Cache-Control"] = CacheControlHeaderValue,
				}),
			cancellationToken);

		return AppendVersionQuery(GetPublicUrl(objectKey), DateTimeOffset.UtcNow);
	}

	public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		await _minio.RemoveObjectAsync(
			new RemoveObjectArgs()
				.WithBucket(_settings.BucketName)
				.WithObject(PublicPrefix + objectKey),
			cancellationToken);
	}

	public async Task QuarantineAsync(string objectKey, CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		await MoveAsync(PublicPrefix + objectKey, QuarantinePrefix + objectKey, cancellationToken);
	}

	public async Task UnquarantineAsync(string objectKey, CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		await MoveAsync(QuarantinePrefix + objectKey, PublicPrefix + objectKey, cancellationToken);
	}

	private async Task MoveAsync(string sourceKey, string destinationKey, CancellationToken cancellationToken)
	{
		await _minio.CopyObjectAsync(
			new CopyObjectArgs()
				.WithBucket(_settings.BucketName)
				.WithObject(destinationKey)
				.WithCopyObjectSource(
					new CopySourceObjectArgs()
						.WithBucket(_settings.BucketName)
						.WithObject(sourceKey)),
			cancellationToken);

		await _minio.RemoveObjectAsync(
			new RemoveObjectArgs()
				.WithBucket(_settings.BucketName)
				.WithObject(sourceKey),
			cancellationToken);
	}

	internal string GetPublicUrl(string objectKey)
	{
		var baseUrl = (_settings.PublicEndpoint ?? _settings.Endpoint).TrimEnd('/');
		return $"{baseUrl}/{_settings.BucketName}/{PublicPrefix}{objectKey}";
	}

	public string? GetObjectKeyFromPublicUrl(string publicUrl)
	{
		var prefix = GetPublicUrl(string.Empty);
		if (!publicUrl.StartsWith(prefix, StringComparison.Ordinal))
			return null;

		var withoutPrefix = publicUrl[prefix.Length..];
		var queryIndex = withoutPrefix.IndexOf('?');
		return queryIndex >= 0 ? withoutPrefix[..queryIndex] : withoutPrefix;
	}

	public async Task PingAsync(CancellationToken cancellationToken = default) =>
		await _minio.BucketExistsAsync(
			new BucketExistsArgs().WithBucket(_settings.BucketName),
			cancellationToken);

	// Object keys don't change on re-upload, so the version query param is
	// what invalidates a browser's cached copy once the underlying object
	// changes - without it, CacheControlHeaderValue's max-age would let a
	// stale image survive a re-upload until it happened to expire.
	internal static string AppendVersionQuery(string url, DateTimeOffset uploadedOn) =>
		$"{url}?v={uploadedOn.ToUnixTimeSeconds()}";

	private async Task EnsureBucketReadyAsync(CancellationToken cancellationToken)
	{
		if (_bucketReady) return;

		await _initLock.WaitAsync(cancellationToken);
		try
		{
			if (_bucketReady) return;

			var exists = await _minio.BucketExistsAsync(
				new BucketExistsArgs().WithBucket(_settings.BucketName),
				cancellationToken);

			if (!exists)
			{
				await _minio.MakeBucketAsync(
					new MakeBucketArgs().WithBucket(_settings.BucketName),
					cancellationToken);
			}

			var policy = $"{{\"Version\":\"2012-10-17\",\"Statement\":[{{\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::{_settings.BucketName}/{PublicPrefix}*\"]}}]}}";

			await _minio.SetPolicyAsync(
				new SetPolicyArgs()
					.WithBucket(_settings.BucketName)
					.WithPolicy(policy),
				cancellationToken);

			_bucketReady = true;
		}
		finally
		{
			_initLock.Release();
		}
	}
}
