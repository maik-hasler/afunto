using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Application.Common.Storage;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

internal sealed class S3FileStorageService : IFileStorageService, IDisposable
{
	internal const string CacheControlHeaderValue = "public, max-age=3600";

	private const string PublicPrefix = "public/";

	// Not covered by the bucket policy set up in EnsureBucketReadyAsync (which
	// only grants anonymous reads under PublicPrefix), so moving an object here
	// makes it unreachable by its old public URL without discarding it - see
	// afunto#2198.
	private const string QuarantinePrefix = "quarantined/";

	private readonly AmazonS3Client _s3;
	private readonly StorageSettings _settings;
	private static readonly SemaphoreSlim _initLock = new(1, 1);
	private static bool _bucketReady;

	public S3FileStorageService(IOptions<StorageSettings> settings)
	{
		_settings = settings.Value;

		// Self-hosted S3 (RustFS, see ADR-13), not AWS: path-style URLs, a
		// fixed signing region, and checksums only where S3 demands them - the
		// SDK's default trailing checksums are an AWS extension that
		// S3-compatible servers are not required to accept.
		var config = new AmazonS3Config
		{
			ServiceURL = _settings.Endpoint,
			ForcePathStyle = true,
			AuthenticationRegion = "us-east-1",
			RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
			ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
		};

		_s3 = new AmazonS3Client(new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey), config);
	}

	public async Task<string> UploadAsync(
		string objectKey,
		Stream content,
		long size,
		string contentType,
		CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		var request = new PutObjectRequest
		{
			BucketName = _settings.BucketName,
			Key = PublicPrefix + objectKey,
			InputStream = content,
			ContentType = contentType,
			// The caller opened the stream, so the caller disposes it.
			AutoCloseStream = false,
		};
		request.Headers.ContentLength = size;
		request.Headers.CacheControl = CacheControlHeaderValue;

		await _s3.PutObjectAsync(request, cancellationToken);

		return AppendVersionQuery(GetPublicUrl(objectKey), DateTimeOffset.UtcNow);
	}

	public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
	{
		await EnsureBucketReadyAsync(cancellationToken);

		await _s3.DeleteObjectAsync(_settings.BucketName, PublicPrefix + objectKey, cancellationToken);
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

	// S3 has no rename. The copy keeps Content-Type and Cache-Control, because
	// CopyObject's default metadata directive is COPY.
	private async Task MoveAsync(string sourceKey, string destinationKey, CancellationToken cancellationToken)
	{
		await _s3.CopyObjectAsync(
			new CopyObjectRequest
			{
				SourceBucket = _settings.BucketName,
				SourceKey = sourceKey,
				DestinationBucket = _settings.BucketName,
				DestinationKey = destinationKey,
			},
			cancellationToken);

		await _s3.DeleteObjectAsync(_settings.BucketName, sourceKey, cancellationToken);
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

	// A missing bucket still counts as reachable: the first upload creates it.
	public async Task PingAsync(CancellationToken cancellationToken = default) =>
		await BucketExistsAsync(cancellationToken);

	// Object keys don't change on re-upload, so the version query param is
	// what invalidates a browser's cached copy once the underlying object
	// changes - without it, CacheControlHeaderValue's max-age would let a
	// stale image survive a re-upload until it happened to expire.
	internal static string AppendVersionQuery(string url, DateTimeOffset uploadedOn) =>
		$"{url}?v={uploadedOn.ToUnixTimeSeconds()}";

	public void Dispose() => _s3.Dispose();

	private async Task<bool> BucketExistsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _s3.HeadBucketAsync(new HeadBucketRequest { BucketName = _settings.BucketName }, cancellationToken);
			return true;
		}
		catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
		{
			return false;
		}
	}

	private async Task EnsureBucketReadyAsync(CancellationToken cancellationToken)
	{
		if (_bucketReady) return;

		await _initLock.WaitAsync(cancellationToken);
		try
		{
			if (_bucketReady) return;

			if (!await BucketExistsAsync(cancellationToken))
			{
				await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _settings.BucketName }, cancellationToken);
			}

			var policy = $"{{\"Version\":\"2012-10-17\",\"Statement\":[{{\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::{_settings.BucketName}/{PublicPrefix}*\"]}}]}}";

			await _s3.PutBucketPolicyAsync(
				new PutBucketPolicyRequest
				{
					BucketName = _settings.BucketName,
					Policy = policy,
				},
				cancellationToken);

			_bucketReady = true;
		}
		finally
		{
			_initLock.Release();
		}
	}
}
