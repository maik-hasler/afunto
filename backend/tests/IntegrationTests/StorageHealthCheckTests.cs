using AwesomeAssertions;
using Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
public class StorageHealthCheckTests(IntegrationTestFixture fixture)
{
	[Test]
	public async Task PingAsync_ShouldSucceed_WhenStorageIsReachable(CancellationToken cancellationToken)
	{
		using var storage = new S3FileStorageService(Options.Create(new StorageSettings
		{
			Endpoint = fixture.GetStorageEndpoint(),
			AccessKey = "storage",
			SecretKey = "storage123",
			BucketName = "afunto",
		}));

		var act = () => storage.PingAsync(cancellationToken);

		await act.Should().NotThrowAsync();
	}

	[Test]
	public async Task PingAsync_ShouldThrow_WhenStorageIsUnreachable(CancellationToken cancellationToken)
	{
		using var storage = new S3FileStorageService(Options.Create(new StorageSettings
		{
			Endpoint = "http://127.0.0.1:1",
			AccessKey = "storage",
			SecretKey = "storage123",
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
