using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetUserProfileTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetUserProfile_ShouldReturnProfile_WhenAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var result = await client.GetUserProfileAsync(cancellationToken);

		result.Id.Should().NotBeEmpty();
		result.Username.Should().Be("vera");
		result.Email.Should().NotBeNullOrEmpty();
	}

	[Test]
	public async Task GetUserProfile_ShouldReturnProfile_WhenAuthenticatedAsAdmin(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var result = await client.GetUserProfileAsync(cancellationToken);

		result.Id.Should().NotBeEmpty();
		result.Username.Should().Be("admin");
	}

	[Test]
	public async Task GetUserProfile_ShouldNotFail_WhenTwoConcurrentRequestsRaceTheFirstEverLoad(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var first = client.GetUserProfileAsync(cancellationToken);
		var second = client.GetUserProfileAsync(cancellationToken);

		var results = await Task.WhenAll(first, second);

		results[0].Id.Should().Be(results[1].Id);
	}

	[Test]
	public async Task GetUserProfile_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.GetUserProfileAsync(cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}
}
