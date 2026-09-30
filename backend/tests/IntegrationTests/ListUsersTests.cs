using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class ListUsersTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task ListUsers_ShouldReturnEachUsersOwnRoles_WhenFetchedConcurrently(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var result = await client.ListUsersAsync(
			pageNumber: 1, pageSize: 100, cancellationToken: cancellationToken);

		var vera = result.Items.Single(u => u.Username == "vera");
		vera.RealmRoles.Should().BeEquivalentTo(["user"]);

		var olaf = result.Items.Single(u => u.Username == "olaf");
		olaf.RealmRoles.Should().BeEquivalentTo(["user", "organisator"]);

		var admin = result.Items.Single(u => u.Username == "admin");
		admin.RealmRoles.Should().BeEquivalentTo(["admin", "user", "organisator"]);
	}

	[Test]
	public async Task ListUsers_ShouldFilterBySearchTerm(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var result = await client.ListUsersAsync(
			pageNumber: 1, pageSize: 10, search: "vera", cancellationToken: cancellationToken);

		result.Items.Should().ContainSingle().Which.Username.Should().Be("vera");
	}

	[Test]
	public async Task ListUsers_ShouldReturn403_WhenNotAdmin(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => client.ListUsersAsync(
			pageNumber: 1, pageSize: 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task ListUsers_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.ListUsersAsync(
			pageNumber: 1, pageSize: 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}
}
