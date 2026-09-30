using System.Net;
using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetVolunteerOpportunityMetaTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetVolunteerOpportunityMeta_ShouldReturnNotFound_WhenOpportunityDoesNotExist(
		CancellationToken cancellationToken)
	{
		using var httpClient = fixture.CreateHttpClient();

		var response = await httpClient.GetAsync(
			$"/v1/meta/volunteer-opportunities/{Guid.NewGuid()}", cancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetVolunteerOpportunityMeta_ShouldReturnNotFound_WhenOpportunityIsDraft(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);
		var draft = await CreateOpportunityAsync(authenticatedClient, orgId, isDraft: true, cancellationToken);

		using var httpClient = fixture.CreateHttpClient();
		var response = await httpClient.GetAsync(
			$"/v1/meta/volunteer-opportunities/{draft.Id}", cancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetVolunteerOpportunityMeta_ShouldReturnHtml_WithOpportunityTitleAndDescription(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(authenticatedClient, orgId, isDraft: false, cancellationToken);

		using var httpClient = fixture.CreateHttpClient();
		var response = await httpClient.GetAsync(
			$"/v1/meta/volunteer-opportunities/{opportunity.Id}", cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);

		response.EnsureSuccessStatusCode();
		response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
		html.Should().Contain("Strandreinigung Musterstadt");
		html.Should().Contain("Gemeinsam sammeln wir Müll am Strand ein.");
		html.Should().Contain($"/volunteer-opportunities/{opportunity.Id}");

		html.Should().Contain("/og-image.png");
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"Testorg_{Guid.NewGuid()}";
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return organization.Id.Value;
	}

	private static Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, bool isDraft, CancellationToken cancellationToken) =>
		client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Strandreinigung Musterstadt",
			DescriptionDe = "Gemeinsam sammeln wir Müll am Strand ein.",
			OrganizationId = orgId,
			IsRemote = true,
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			IsDraft = isDraft,
		}, cancellationToken);
}
