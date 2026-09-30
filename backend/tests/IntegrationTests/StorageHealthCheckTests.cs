using AwesomeAssertions;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
public class StorageHealthCheckTests(IntegrationTestFixture fixture)
{
	[Test]
	public async Task PingAsync_ShouldSucceed_WhenMinioIsReachable(CancellationToken cancellationToken)
	{
		var storage = new MinioFileStorageService(Options.Create(new StorageSettings
		{
			Endpoint = fixture.GetMinioEndpoint(),
			AccessKey = "minio",
			SecretKey = "minio123",
			BucketName = "afunto",
		}));

		var act = () => storage.PingAsync(cancellationToken);

		await act.Should().NotThrowAsync();
	}

	[Test]
	public async Task PingAsync_ShouldThrow_WhenMinioIsUnreachable(CancellationToken cancellationToken)
	{
		var storage = new MinioFileStorageService(Options.Create(new StorageSettings
		{
			Endpoint = "http://127.0.0.1:1",
			AccessKey = "minio",
			SecretKey = "minio123",
			BucketName = "afunto",
		}));

		var act = () => storage.PingAsync(cancellationToken);

		await act.Should().ThrowAsync<Exception>();
	}

	[Test]
	public async Task GetHealth_ShouldReturnHealthy_WhenStorageIsReachable(CancellationToken cancellationToken)
	{
		using var httpClient = fixture.CreateHttpClient();

		var response = await httpClient.GetAsync("/health", cancellationToken);

		response.IsSuccessStatusCode.Should().BeTrue();
	}
}
