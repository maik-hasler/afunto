using System.Net.Http.Headers;
using AwesomeAssertions;
using TUnit.Core.Interfaces;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class OrganizationCalendarEventsTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturnOnlySlotsWithinRange_ExcludingSlotsOutsideRange(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsDraftOpportunityAsync(olafClient, orgId, cancellationToken);

		var inRangeStart = DateTimeOffset.UtcNow.AddDays(10);
		var inRangeSlot = (await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = inRangeStart,
				EndDateTime = inRangeStart.AddHours(2),
				MaxParticipants = 5,
				RecurrenceCount = 1,
			},
			cancellationToken)).Single().Id;

		var outOfRangeStart = DateTimeOffset.UtcNow.AddDays(90);
		await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = outOfRangeStart,
				EndDateTime = outOfRangeStart.AddHours(2),
				MaxParticipants = 5,
				RecurrenceCount = 1,
			},
			cancellationToken);

		var events = await olafClient.GetOrganizationCalendarEventsAsync(
			orgId,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		var evt = events.Single();
		evt.OpportunityId.Should().Be(opportunity.Id);
		evt.TimeSlots.Select(ts => ts.TimeSlotId).Should().BeEquivalentTo([inRangeSlot]);
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturnBookedCount_ForPendingAndConfirmedEngagementsOnly(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsDraftOpportunityAsync(olafClient, orgId, cancellationToken);

		var slotStart = DateTimeOffset.UtcNow.AddDays(5);
		var slotId = (await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = slotStart,
				EndDateTime = slotStart.AddHours(2),
				MaxParticipants = 5,
				RecurrenceCount = 1,
			},
			cancellationToken)).Single().Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);

		var toBeCancelled = await olafClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);
		await olafClient.CancelEngagementAsync(
			toBeCancelled.Id,
			new CancelEngagementRequest { Reason = "No longer needed" },
			cancellationToken);

		var events = await olafClient.GetOrganizationCalendarEventsAsync(
			orgId,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		events.Single().TimeSlots.Single().BookedCount.Should().Be(1);
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturnEmptyList_WhenNoSlotsFallWithinRange(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var events = await olafClient.GetOrganizationCalendarEventsAsync(
			orgId,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		events.Should().BeEmpty();
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturn400_WhenToIsBeforeFrom(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.GetOrganizationCalendarEventsAsync(
			orgId,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(-1),
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var anonClient = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => anonClient.GetOrganizationCalendarEventsAsync(
			Guid.NewGuid(),
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldReturn403_WhenOrganisatorAccessesOtherOrgsEvents(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);

		var veraToken = await fixture.GetAccessTokenAsync("vera", "vera123");
		var veraHttpClient = fixture.CreateHttpClient();
		veraHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", veraToken);
		var veraClient = new AfuntoApi(veraHttpClient);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.GetOrganizationCalendarEventsAsync(
			org1Id,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task GetOrganizationCalendarEvents_ShouldSucceed_WhenRequestingUserIsAPlainMember(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		await fixture.AddPlainMemberDirectlyAsync(orgId, vera.Id, cancellationToken);

		var events = await veraClient.GetOrganizationCalendarEventsAsync(
			orgId,
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken);

		events.Should().BeEmpty();
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"CalendarEventsTestOrg_{Guid.NewGuid()}";
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return org.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateScheduledSlotsDraftOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Calendar Events Test Opportunity",
				DescriptionDe = "Integration test opportunity",
				OrganizationId = orgId,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
				Occurrence = "Recurring",
				ParticipationType = "ScheduledSlots",
				CheckInMethod = "None",
				IsDraft = true,
			},
			cancellationToken);
	}
}
