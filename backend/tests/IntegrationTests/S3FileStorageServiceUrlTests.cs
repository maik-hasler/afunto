using AwesomeAssertions;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

public class S3FileStorageServiceUrlTests
{
	private static S3FileStorageService CreateSut() =>
		new(Options.Create(new StorageSettings
		{
			Endpoint = "http://storage:9000",
			AccessKey = "access-key",
			SecretKey = "secret-key",
			BucketName = "afunto",
			PublicEndpoint = "https://storage.example.com",
		}));

	[Test]
	public void GetPublicUrl_ShouldPrefixWithPublicEndpointBucketAndPublicPrefix()
	{
		using var sut = CreateSut();

		var result = sut.GetPublicUrl("user-avatars/user-1/abc.png");

		result.Should().Be("https://storage.example.com/afunto/public/user-avatars/user-1/abc.png");
	}

	[Test]
	public void GetObjectKeyFromPublicUrl_ShouldRecoverTheObjectKey_PassedToGetPublicUrl()
	{
		using var sut = CreateSut();
		const string objectKey = "user-avatars/user-1/abc.png";

		var url = sut.GetPublicUrl(objectKey);
		var result = sut.GetObjectKeyFromPublicUrl(url);

		result.Should().Be(objectKey);
	}

	[Test]
	public void GetObjectKeyFromPublicUrl_ShouldStripTheVersionQueryParam()
	{
		using var sut = CreateSut();
		const string objectKey = "user-avatars/user-1/abc.png";
		var uploadedOn = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
		var versionedUrl = S3FileStorageService.AppendVersionQuery(sut.GetPublicUrl(objectKey), uploadedOn);

		var result = sut.GetObjectKeyFromPublicUrl(versionedUrl);

		result.Should().Be(objectKey);
	}

	[Test]
	public void GetObjectKeyFromPublicUrl_ShouldReturnNull_WhenUrlDoesNotMatchThisServicesPublicUrlFormat()
	{
		using var sut = CreateSut();

		var result = sut.GetObjectKeyFromPublicUrl("https://not-our-storage.example.com/some/other/path.png");

		result.Should().BeNull();
	}

	[Test]
	public void AppendVersionQuery_ShouldAppendUnixSecondsAsVersionQueryParam()
	{
		var uploadedOn = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

		var result = S3FileStorageService.AppendVersionQuery(
			"https://storage.example.com/bucket/user-avatars/abc.png", uploadedOn);

		result.Should().Be($"https://storage.example.com/bucket/user-avatars/abc.png?v={uploadedOn.ToUnixTimeSeconds()}");
	}

	[Test]
	public void AppendVersionQuery_ShouldProduceDifferentVersions_WhenReuploadedAtDifferentTimes()
	{
		const string url = "https://storage.example.com/bucket/user-avatars/abc.png";
		var firstUpload = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
		var secondUpload = firstUpload.AddSeconds(1);

		var firstUrl = S3FileStorageService.AppendVersionQuery(url, firstUpload);
		var secondUrl = S3FileStorageService.AppendVersionQuery(url, secondUpload);

		firstUrl.Should().NotBe(secondUrl);
	}

	[Test]
	public void CacheControlHeaderValue_ShouldBePublicWithModerateMaxAge()
	{
		S3FileStorageService.CacheControlHeaderValue.Should().Be("public, max-age=3600");
	}
}
