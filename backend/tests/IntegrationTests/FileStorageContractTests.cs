using System.Net;
using AwesomeAssertions;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

// Pins the behaviour the app relies on from whichever S3 server the AppHost
// runs, through the real S3FileStorageService: anonymous reads under
// public/ only, Cache-Control kept on the object, no anonymous listing or
// writes. These are exactly the checks ADR-13 used to choose RustFS, so a
// future swap of the storage image fails here instead of in production.
[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
public class FileStorageContractTests(IntegrationTestFixture fixture)
{
	private static readonly byte[] Content = "afunto storage contract"u8.ToArray();

	private static readonly HttpClient Anonymous = new();

	private S3FileStorageService CreateStorage() =>
		new(Options.Create(new StorageSettings
		{
			Endpoint = fixture.GetStorageEndpoint(),
			AccessKey = "storage",
			SecretKey = "storage123",
			BucketName = "afunto",
		}));

	private string BucketUrl => $"{fixture.GetStorageEndpoint().TrimEnd('/')}/afunto";

	private static string NewObjectKey() => $"contract-tests/{Guid.NewGuid():N}.png";

	private static async Task<string> UploadAsync(S3FileStorageService storage, string objectKey, CancellationToken cancellationToken) =>
		await storage.UploadAsync(objectKey, new MemoryStream(Content), Content.Length, "image/png", cancellationToken);

	[Test]
	public async Task UploadAsync_ShouldBeAnonymouslyReadable_WithContentTypeAndCacheControl(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		var url = await UploadAsync(storage, NewObjectKey(), cancellationToken);

		using var response = await Anonymous.GetAsync(url, cancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
		response.Headers.CacheControl?.ToString().Should().Be(S3FileStorageService.CacheControlHeaderValue);
		(await response.Content.ReadAsByteArrayAsync(cancellationToken)).Should().Equal(Content);
	}

	[Test]
	public async Task QuarantineAsync_ShouldMakeTheObjectUnreachableAnonymously(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		var objectKey = NewObjectKey();
		var url = await UploadAsync(storage, objectKey, cancellationToken);

		await storage.QuarantineAsync(objectKey, cancellationToken);

		using var oldUrl = await Anonymous.GetAsync(url, cancellationToken);
		using var quarantinedPath = await Anonymous.GetAsync($"{BucketUrl}/quarantined/{objectKey}", cancellationToken);
		oldUrl.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
		quarantinedPath.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task UnquarantineAsync_ShouldRestoreAnonymousRead_WithContentTypeAndCacheControl(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		var objectKey = NewObjectKey();
		var url = await UploadAsync(storage, objectKey, cancellationToken);
		await storage.QuarantineAsync(objectKey, cancellationToken);

		await storage.UnquarantineAsync(objectKey, cancellationToken);

		using var response = await Anonymous.GetAsync(url, cancellationToken);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
		response.Headers.CacheControl?.ToString().Should().Be(S3FileStorageService.CacheControlHeaderValue);
	}

	[Test]
	public async Task DeleteAsync_ShouldMakeTheObjectUnreachable(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		var objectKey = NewObjectKey();
		var url = await UploadAsync(storage, objectKey, cancellationToken);

		await storage.DeleteAsync(objectKey, cancellationToken);

		using var response = await Anonymous.GetAsync(url, cancellationToken);
		response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Bucket_ShouldRefuseAnonymousListing(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		await UploadAsync(storage, NewObjectKey(), cancellationToken);

		using var response = await Anonymous.GetAsync($"{BucketUrl}/", cancellationToken);

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Bucket_ShouldRefuseAnonymousListingOfThePublicPrefix(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		await UploadAsync(storage, NewObjectKey(), cancellationToken);

		using var response = await Anonymous.GetAsync($"{BucketUrl}?list-type=2&prefix=public/", cancellationToken);

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task Bucket_ShouldRefuseAnonymousWrites(CancellationToken cancellationToken)
	{
		using var storage = CreateStorage();
		await UploadAsync(storage, NewObjectKey(), cancellationToken);

		using var response = await Anonymous.PutAsync($"{BucketUrl}/public/contract-tests/anonymous.txt", new StringContent("x"), cancellationToken);

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
	}
}
