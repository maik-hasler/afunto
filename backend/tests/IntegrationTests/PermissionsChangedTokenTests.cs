using AwesomeAssertions;

namespace IntegrationTests;

/// <summary>
/// A token issued before someone else changed the user's permissions is
/// rejected, so a demotion, a block or a promotion takes effect on the very
/// next request instead of when the token expires (#2395, #2403, #2407).
/// </summary>
[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class PermissionsChangedTokenTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task AdminEndpoint_ShouldReturn401ForTheOldToken_WhenAdminRoleWasRemoved(
		CancellationToken cancellationToken)
	{
		// Arrange
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		var (userId, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		await adminClient.SetUserAdminStatusAsync(
			userId, new SetUserAdminStatusRequest { IsAdmin = true }, cancellationToken);
		var demotedClient = await fixture.CreateAuthenticatedClientAsync(username, password);
		await demotedClient.ListUsersAsync(1, 20, null, cancellationToken);
		await WaitForNextTokenSecondAsync(cancellationToken);

		// Act
		await adminClient.SetUserAdminStatusAsync(
			userId, new SetUserAdminStatusRequest { IsAdmin = false }, cancellationToken);
		var act = () => demotedClient.ListUsersAsync(1, 20, null, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task AdminEndpoint_ShouldReturn403ForAFreshToken_WhenAdminRoleWasRemoved(
		CancellationToken cancellationToken)
	{
		// Arrange
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		var (userId, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		await adminClient.SetUserAdminStatusAsync(
			userId, new SetUserAdminStatusRequest { IsAdmin = true }, cancellationToken);
		await adminClient.SetUserAdminStatusAsync(
			userId, new SetUserAdminStatusRequest { IsAdmin = false }, cancellationToken);

		// Act - no wait: a token issued in the second of the change counts as fresh
		var freshClient = await fixture.CreateAuthenticatedClientAsync(username, password);
		var act = () => freshClient.ListUsersAsync(1, 20, null, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task AnyEndpoint_ShouldReturn401ForTheOldToken_WhenUserWasBlocked(
		CancellationToken cancellationToken)
	{
		// Arrange
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");
		var (userId, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var blockedClient = await fixture.CreateAuthenticatedClientAsync(username, password);
		await blockedClient.GetUserProfileAsync(cancellationToken);
		await WaitForNextTokenSecondAsync(cancellationToken);

		// Act
		await adminClient.SetUserEnabledAsync(
			userId, new SetUserEnabledRequest { Enabled = false }, cancellationToken);
		var act = () => blockedClient.GetUserProfileAsync(cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task OrganizerEndpoint_ShouldSucceedWithAFreshToken_WhenMemberWasPromoted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var (_, ownerUsername, ownerPassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var (memberId, memberUsername, memberPassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);

		var ownerClient = await fixture.CreateAuthenticatedClientAsync(ownerUsername, ownerPassword);
		var org = await ownerClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Promotion {Guid.NewGuid()}" }, cancellationToken);
		ownerClient = await fixture.CreateAuthenticatedClientAsync(ownerUsername, ownerPassword);
		var invitation = await ownerClient.CreateInvitationAsync(
			org.Id.Value,
			new CreateInvitationRequest { InviteeId = memberId, Role = "Member" },
			cancellationToken);
		var memberClient = await fixture.CreateAuthenticatedClientAsync(memberUsername, memberPassword);
		await memberClient.AcceptInvitationAsync(invitation.InvitationId, cancellationToken);
		await WaitForNextTokenSecondAsync(cancellationToken);

		// Act
		await ownerClient.ChangeMemberRoleAsync(
			org.Id.Value, memberId, new ChangeMemberRoleRequest { Role = "Organizer" }, cancellationToken);
		var oldTokenAct = () => memberClient.GetOrganizationDetailsAsync(org.Id.Value, cancellationToken);
		var freshClient = await fixture.CreateAuthenticatedClientAsync(memberUsername, memberPassword);
		var details = await freshClient.GetOrganizationDetailsAsync(org.Id.Value, cancellationToken);

		// Assert
		(await oldTokenAct.Should().ThrowAsync<ApiException>()).Which.StatusCode.Should().Be(401);
		details.Id.Should().Be(org.Id.Value);
	}

	// `iat` has whole-second precision and the API treats a token stamped in the
	// same second as the change as issued after it - a token meant to count as
	// old must be fetched at least one second before the change.
	private static Task WaitForNextTokenSecondAsync(CancellationToken cancellationToken) =>
		Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken);
}
