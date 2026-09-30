using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class SearchMemberCandidatesTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	[Arguments("v")]
	[Arguments("ve")]
	[Arguments("ver")]
	public async Task SearchMemberCandidates_ShouldReturnNothing_WhenQueryIsShorterThanFourCharacters(
		string query,
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, query, cancellationToken);

		candidates.Should().BeEmpty();
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldFindTheUser_WhenQueryIsTheExactUsername(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, "vera", cancellationToken);

		var candidate = candidates.Should().ContainSingle().Which;
		candidate.Username.Should().Be("vera");
		candidate.Status.Should().Be("Available");
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldFindTheUser_WhenQueryIsTheExactEmail(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var (userId, username, _) = await fixture.CreateEphemeralUserAsync(cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, $"{username}@example.com", cancellationToken);

		candidates.Should().ContainSingle(c => c.UserId == userId);
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldFindNobody_WhenQueryIsOnlyAPrefixOfTheUsername(
		CancellationToken cancellationToken)
	{
		// This is the regression test for the realm-wide enumeration this endpoint used to allow
		// (#2205): a query that used to match dozens of users via Keycloak's infix `search` must
		// now match nobody unless it names one user's full username or email exactly.
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var (_, username, _) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var prefix = username[..^1];

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, prefix, cancellationToken);

		candidates.Should().BeEmpty();
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldReportAlreadyMember_WhenTheExactMatchIsAlreadyInTheOrganization(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var (userId, username, _) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		await fixture.AddPlainMemberDirectlyAsync(organizationId, userId, cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, username, cancellationToken);

		candidates.Should().ContainSingle().Which.Status.Should().Be("AlreadyMember");
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldReportAlreadyInvited_WhenTheExactMatchHasAPendingInvitation(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var (userId, username, _) = await fixture.CreateEphemeralUserAsync(cancellationToken);

		await client.CreateInvitationAsync(
			organizationId, new CreateInvitationRequest { InviteeId = userId, Role = "Member" }, cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, username, cancellationToken);

		candidates.Should().ContainSingle().Which.Status.Should().Be("AlreadyInvited");
	}

	[Test]
	public async Task SearchMemberCandidates_ShouldNeverCarryAnEmailAddress(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);

		var candidates = await client.SearchMemberCandidatesAsync(
			organizationId, "vera", cancellationToken);

		var candidate = candidates.Should().ContainSingle().Which;

		typeof(MemberCandidateDto).GetProperties()
			.Select(property => property.Name)
			.Should().BeEquivalentTo(
				"UserId", "Username", "FirstName", "LastName", "Status", "AdditionalProperties");

		candidate.AdditionalProperties.Should().BeEmpty();

		new[] { candidate.Username, candidate.FirstName, candidate.LastName }
			.Should().NotContain(value => value != null && value.Contains('@'));
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Member search {Guid.NewGuid()}" },
			cancellationToken);
		return organization.Id.Value;
	}
}
