using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class ListOrganizationsFilterTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task ListOrganizations_ShouldFilterByCaseInsensitiveNameSearch(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var matchName = $"Feuerwehr Alpha {Guid.NewGuid()}";
		var otherName = $"Wasserwacht Beta {Guid.NewGuid()}";
		await CreateOrganizationAsync(olafClient, matchName, cancellationToken);
		await CreateOrganizationAsync(olafClient, otherName, cancellationToken);

		var result = await adminClient.ListOrganizationsAsync(
			1, 50, search: "alpha", cancellationToken: cancellationToken);

		result.Items.Should().ContainSingle(o => o.Name == matchName);
	}

	[Test]
	public async Task ListOrganizations_ShouldExcludeDeletedOrganizations_ByDefault(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var name = $"ToDelete {Guid.NewGuid()}";
		var orgId = await CreateOrganizationAsync(olafClient, name, cancellationToken);
		await adminClient.AdminShadowDeleteOrganizationAsync(orgId, cancellationToken);

		var result = await adminClient.ListOrganizationsAsync(
			1, 50, search: name, cancellationToken: cancellationToken);

		result.Items.Should().BeEmpty();
	}

	[Test]
	public async Task ListOrganizations_ShouldReturnOnlyDeletedOrganizations_WhenDeletedFilterIsTrue(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var deletedName = $"Deleted {Guid.NewGuid()}";
		var activeName = $"Active {Guid.NewGuid()}";
		var deletedId = await CreateOrganizationAsync(olafClient, deletedName, cancellationToken);
		await CreateOrganizationAsync(olafClient, activeName, cancellationToken);
		await adminClient.AdminShadowDeleteOrganizationAsync(deletedId, cancellationToken);

		var result = await adminClient.ListOrganizationsAsync(
			1, 50, deleted: true, cancellationToken: cancellationToken);

		var deletedOrg = result.Items.Should().ContainSingle(o => o.Id == deletedId).Which;
		deletedOrg.IsDeleted.Should().BeTrue();
	}

	[Test]
	public async Task ListOrganizations_ShouldReturnOnlyFlaggedOrganizations_WhenFlaggedFilterIsTrue(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var flaggedName = $"Flagged {Guid.NewGuid()}";
		var unflaggedName = $"Unflagged {Guid.NewGuid()}";
		var flaggedId = await CreateOrganizationAsync(olafClient, flaggedName, cancellationToken);
		await CreateOrganizationAsync(olafClient, unflaggedName, cancellationToken);

		await veraClient.ReportOrganizationAsync(
			flaggedId,
			new ReportOrganizationRequest { Reason = "Other", Details = "Integration test report" },
			cancellationToken);

		var result = await adminClient.ListOrganizationsAsync(
			1, 50, flagged: true, cancellationToken: cancellationToken);

		var flaggedOrg = result.Items.Should().ContainSingle(o => o.Id == flaggedId).Which;
		flaggedOrg.OpenReportCount.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task ListOrganizations_ShouldPopulateMemberCount_ForOrganizationWithMultipleMembers(
		CancellationToken cancellationToken)
	{
		// Arrange

		var (_, organizerUsername, organizerPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var (memberOneId, memberOneUsername, memberOnePassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var (memberTwoId, memberTwoUsername, memberTwoPassword) =
			await fixture.CreateEphemeralUserAsync(cancellationToken);
		var adminClient = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var organizerClient = await fixture.CreateAuthenticatedClientAsync(organizerUsername, organizerPassword);
		var orgName = $"Multi Member Org {Guid.NewGuid()}";
		var org = await organizerClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = orgName }, cancellationToken);

		organizerClient = await fixture.CreateAuthenticatedClientAsync(organizerUsername, organizerPassword);

		var invitationOne = await organizerClient.CreateInvitationAsync(
			org.Id.Value,
			new CreateInvitationRequest { InviteeId = memberOneId, Role = "Member" },
			cancellationToken);
		var memberOneClient = await fixture.CreateAuthenticatedClientAsync(memberOneUsername, memberOnePassword);
		await memberOneClient.AcceptInvitationAsync(invitationOne.InvitationId, cancellationToken);

		var invitationTwo = await organizerClient.CreateInvitationAsync(
			org.Id.Value,
			new CreateInvitationRequest { InviteeId = memberTwoId, Role = "Member" },
			cancellationToken);
		var memberTwoClient = await fixture.CreateAuthenticatedClientAsync(memberTwoUsername, memberTwoPassword);
		await memberTwoClient.AcceptInvitationAsync(invitationTwo.InvitationId, cancellationToken);

		// Act
		var result = await adminClient.ListOrganizationsAsync(
			1, 50, search: orgName, cancellationToken: cancellationToken);

		// Assert
		var summary = result.Items.Should().ContainSingle(o => o.Id == org.Id.Value).Which;
		summary.MemberCount.Should().Be(3);
		summary.CreatedOn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, string name, CancellationToken cancellationToken)
	{
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = name }, cancellationToken);
		return org.Id.Value;
	}
}
