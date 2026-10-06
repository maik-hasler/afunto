using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class AdminEndpointAuthorizationTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task ListOrganizations_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.ListOrganizationsAsync(1, 20, cancellationToken: cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task ListOrganizations_ShouldReturn403_WhenRequestingUserIsNotAdmin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var act = () => olafClient.ListOrganizationsAsync(1, 20, cancellationToken: cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task ListUsers_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.ListUsersAsync(1, 20, null, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task ListUsers_ShouldReturn403_WhenRequestingUserIsNotAdmin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var act = () => olafClient.ListUsersAsync(1, 20, null, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task SetUserAdminStatus_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.SetUserAdminStatusAsync(
			Guid.NewGuid(), new SetUserAdminStatusRequest { IsAdmin = true }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task SetUserAdminStatus_ShouldReturn403_WhenRequestingUserIsNotAdmin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);

		var act = () => olafClient.SetUserAdminStatusAsync(
			vera.Id, new SetUserAdminStatusRequest { IsAdmin = true }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task SetUserEnabled_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.SetUserEnabledAsync(
			Guid.NewGuid(), new SetUserEnabledRequest { Enabled = false }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task SetUserEnabled_ShouldReturn403_WhenRequestingUserIsNotAdmin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);

		var act = () => olafClient.SetUserEnabledAsync(
			vera.Id, new SetUserEnabledRequest { Enabled = false }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task ListAuditLogs_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.ListAuditLogsAsync(1, 20, cancellationToken: cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task ListAuditLogs_ShouldReturn403_WhenRequestingUserIsNotAdmin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var act = () => olafClient.ListAuditLogsAsync(1, 20, cancellationToken: cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}
}
