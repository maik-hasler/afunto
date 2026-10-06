using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetPublicOrganizationsTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetPublicOrganizations_ShouldCountOnlyPublishedOpportunities(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, "OpenCount", cancellationToken);

		await PublishOpportunityAsync(client, organizationId, "Published one", cancellationToken);

		await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Draft, never published",
			DescriptionDe = "Must not be counted as open.",
			OrganizationId = organizationId,
			IsRemote = true,
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			IsDraft = true,
		}, cancellationToken);

		var directory = new AfuntoApi(fixture.CreateHttpClient());

		var page = await directory.GetPublicOrganizationsAsync(
			1, 10, search: "OpenCount", cancellationToken: cancellationToken);

		page.Items.Should().ContainSingle()
			.Which.OpenOpportunityCount.Should().Be(1);
	}

	[Test]
	public async Task GetPublicOrganizations_ShouldReportZeroOpenOpportunities_ForABareOrganization(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		await CreateOrganizationAsync(client, "BareOrg", cancellationToken);

		var directory = new AfuntoApi(fixture.CreateHttpClient());

		var page = await directory.GetPublicOrganizationsAsync(
			1, 10, search: "BareOrg", cancellationToken: cancellationToken);

		page.Items.Should().ContainSingle()
			.Which.OpenOpportunityCount.Should().Be(0);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, string namePrefix, CancellationToken cancellationToken)
	{
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"{namePrefix} {Guid.NewGuid():N}" },
			cancellationToken);
		return organization.Id.Value;
	}

	private static async Task PublishOpportunityAsync(
		AfuntoApi client, Guid organizationId, string title, CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = title,
				DescriptionDe = "Coverage for the directory's open-opportunity count.",
				OrganizationId = organizationId,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
				IsDraft = true,
			}, cancellationToken);

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);
	}
}
