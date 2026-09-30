using AwesomeAssertions;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class VolunteerOpportunityOwnershipTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetOpportunityCheckInPin_ShouldReturn403_WhenOrganizerAccessesOtherOrgsOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken, checkInPin: "135790");

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.GetOpportunityCheckInPinAsync(opportunity.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task DeleteVolunteerOpportunity_ShouldReturn403_WhenOrganizerDeletesOtherOrgsOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);

		var stillExists = await olafClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);
		stillExists.Should().NotBeNull();
	}

	[Test]
	public async Task UpdateTimeSlot_ShouldReturn403_WhenOrganizerUpdatesOtherOrgsTimeSlot(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);
		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
			},
			cancellationToken);
		var timeSlotId = timeSlots.Single().Id;

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.UpdateTimeSlotAsync(
			opportunity.Id,
			timeSlotId,
			new UpdateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(14),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(14).AddHours(2),
				MaxParticipants = 99,
			},
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"OwnershipTestOrg_{Guid.NewGuid()}";
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return org.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken, string? checkInPin = null)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Test Opportunity",
				DescriptionDe = "Integration test opportunity",
				OrganizationId = orgId,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
				Occurrence = "Recurring",
				ParticipationType = "ScheduledSlots",
				CheckInMethod = checkInPin is null ? "None" : "PINCode",
				CheckInPin = checkInPin,
				IsDraft = true,
			},
			cancellationToken);
	}
}
