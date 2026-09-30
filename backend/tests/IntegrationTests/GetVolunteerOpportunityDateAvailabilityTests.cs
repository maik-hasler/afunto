using System.Globalization;
using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetVolunteerOpportunityDateAvailabilityTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetDateAvailability_ShouldReturnTheDayASlotStartsOn_WithItsOpportunityCount(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotStart = UtcDayAt(daysFromToday: 5, hour: 10);
		await CreateOpportunityWithTimeSlotAsync(authenticatedClient, orgId, "Park cleanup", slotStart, cancellationToken);
		await CreateOpportunityWithTimeSlotAsync(authenticatedClient, orgId, "River cleanup", slotStart.AddHours(3), cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.1");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken: cancellationToken);

		var day = result.Should().ContainSingle(d => d.Date == slotStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Subject;
		day.OpportunityCount.Should().Be(2, "both opportunities have a slot starting on that day");
	}

	[Test]
	public async Task GetDateAvailability_ShouldCountAnOpportunityOnce_WhenItHasSeveralSlotsOnTheSameDay(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotStart = UtcDayAt(daysFromToday: 6, hour: 9);
		var opportunity = await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Two shifts, one day", slotStart, cancellationToken);

		await authenticatedClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = slotStart.AddHours(5),
				EndDateTime = slotStart.AddHours(7),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.2");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken: cancellationToken);

		var day = result.Should().ContainSingle(d => d.Date == slotStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Subject;
		day.OpportunityCount.Should().Be(1,
			"the count is opportunities to pick from, not shifts on offer");
	}

	[Test]
	public async Task GetDateAvailability_ShouldAttributeASlotToTheCallersLocalDay_NotTheUtcOne(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		// Asia/Kolkata (UTC+5:30, no DST) rather than Europe/Berlin here - this test is
		// about the caller's zone shifting the day at all, not about a DST boundary
		// specifically (the dedicated DST tests below cover that).
		var slotStart = UtcDayAt(daysFromToday: 7, hour: 23, minute: 30);
		await CreateOpportunityWithTimeSlotAsync(authenticatedClient, orgId, "Late night shift", slotStart, cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.3", timezone: "Asia/Kolkata");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken: cancellationToken);

		result.Should().ContainSingle()
			.Which.Date.Should().Be(slotStart.AddHours(5).AddMinutes(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task GetDateAvailability_ShouldBucketByBerlinDay_ForASlotJustAfterTheSpringForwardTransition(
		CancellationToken cancellationToken)
	{
		// Germany's 2027 DST starts 2027-03-28 (clocks 02:00 -> 03:00 CEST); by
		// 2027-03-31 Berlin is at UTC+2. A single offset computed earlier in March
		// (still UTC+1) would shift this slot into the wrong, earlier day (#2203).
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		// 2027-03-31 00:30 Berlin (CEST, UTC+2) as its UTC instant - Npgsql's
		// timestamptz mapping rejects DateTimeOffset values with a non-zero
		// offset, so every persisted instant in these tests is UTC already.
		var slotStart = new DateTimeOffset(2027, 3, 30, 22, 30, 0, TimeSpan.Zero);
		await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Post-spring-forward shift", slotStart, cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.11", timezone: "Europe/Berlin");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			new DateTimeOffset(2027, 3, 25, 0, 0, 0, TimeSpan.Zero),
			new DateTimeOffset(2027, 4, 5, 0, 0, 0, TimeSpan.Zero),
			cancellationToken: cancellationToken);

		result.Should().ContainSingle().Which.Date.Should().Be("2027-03-31");
	}

	[Test]
	public async Task GetDateAvailability_ShouldBucketByBerlinDay_ForASlotJustBeforeTheFallBackTransitionEnds(
		CancellationToken cancellationToken)
	{
		// Germany's 2026 DST ends 2026-10-25 (clocks 03:00 -> 02:00 CET); by
		// 2026-10-31 Berlin is back at UTC+1. A single offset computed earlier in
		// October (still UTC+2) would shift this slot into November instead (#2203).
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		// 2026-10-31 23:30 Berlin (CET, UTC+1) as its UTC instant - see the
		// comment in the spring-forward test above for why this must be UTC.
		var slotStart = new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero);
		await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Pre-november shift", slotStart, cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.12", timezone: "Europe/Berlin");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero),
			new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
			cancellationToken: cancellationToken);

		result.Should().ContainSingle().Which.Date.Should().Be("2026-10-31");
	}

	[Test]
	public async Task GetDateAvailability_ShouldExcludeSlotsOutsideTheRequestedWindow(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var inside = UtcDayAt(daysFromToday: 3, hour: 10);
		var outside = UtcDayAt(daysFromToday: 40, hour: 10);
		await CreateOpportunityWithTimeSlotAsync(authenticatedClient, orgId, "Inside the window", inside, cancellationToken);
		await CreateOpportunityWithTimeSlotAsync(authenticatedClient, orgId, "Outside the window", outside, cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.4");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(10),
			cancellationToken: cancellationToken);

		result.Select(d => d.Date).Should().Equal(inside.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task GetDateAvailability_ShouldNotMarkAnyDay_ForAnOpportunityWithoutTimeSlots(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Express interest opportunity",
			DescriptionDe = "No time slots - IndividualContact",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
		}, cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.5");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken: cancellationToken);

		result.Should().BeEmpty(
			"an opportunity with no dates of its own belongs to no single day - it stays in the results whichever range is picked, so marking every day for it would say nothing");
	}

	[Test]
	public async Task GetDateAvailability_ShouldNotMarkADay_ForAnUnpublishedOpportunity(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotStart = UtcDayAt(daysFromToday: 4, hour: 10);
		var draft = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Still a draft",
			DescriptionDe = "Never published",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

		await authenticatedClient.CreateTimeSlotAsync(
			draft.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = slotStart,
				EndDateTime = slotStart.AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		var sut = CreateAnonymousClient("10.0.3.6");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			cancellationToken: cancellationToken);

		result.Should().BeEmpty(
			"a draft is not in the public listing, so it must not mark a day in the calendar in front of it");
	}

	[Test]
	public async Task GetDateAvailability_ShouldHonourTheSameFilters_AsTheListingItself(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var matching = UtcDayAt(daysFromToday: 3, hour: 10);
		var other = UtcDayAt(daysFromToday: 4, hour: 10);
		await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Beach cleanup", matching, cancellationToken, category: "Environment");
		await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Chess club", other, cancellationToken, category: "Culture");

		var sut = CreateAnonymousClient("10.0.3.7");

		var result = await sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(30),
			categories: ["Environment"],
			cancellationToken: cancellationToken);

		result.Select(d => d.Date).Should().Equal(
			new[] { matching.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
			"a day is only worth marking if the filters already applied would let its opportunity through");
	}

	[Test]
	public async Task GetDateAvailability_ShouldReturnBadRequest_WhenToIsBeforeFrom(
		CancellationToken cancellationToken)
	{
		var sut = CreateAnonymousClient("10.0.3.8");

		var act = () => sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow.AddDays(10),
			DateTimeOffset.UtcNow.AddDays(1),
			cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetDateAvailability_ShouldReturnBadRequest_WhenTheWindowIsWiderThanTwoMonths(
		CancellationToken cancellationToken)
	{
		var sut = CreateAnonymousClient("10.0.3.9");

		var act = () => sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(400),
			cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetDateAvailability_ShouldReturnBadRequest_WhenACategoryIsNotAKnownOne(
		CancellationToken cancellationToken)
	{
		var sut = CreateAnonymousClient("10.0.3.10");

		var act = () => sut.GetVolunteerOpportunityDateAvailabilityAsync(
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow.AddDays(30),
			categories: ["NotACategory"],
			cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400,
			"the listing rejects it, and silently ignoring it here would mark days the listing then refuses to fill");
	}

	private static DateTimeOffset UtcDayAt(int daysFromToday, int hour, int minute = 0) =>
		new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(daysFromToday).Date, TimeSpan.Zero)
			.AddHours(hour)
			.AddMinutes(minute);

	// Stays local while the authenticated helper moved to the fixture: this one is the
	// subject of these tests, not boilerplate. Every case drives the endpoint anonymously
	// from its own X-Forwarded-For (so neighbours' requests cannot exhaust its rate-limit
	// bucket) and, for the timezone cases, its own X-Timezone - per-test headers a shared
	// helper has no business setting.
	private AfuntoApi CreateAnonymousClient(string clientIp, string? timezone = null)
	{
		var httpClient = fixture.CreateHttpClient();
		httpClient.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
		if (timezone is not null)
			httpClient.DefaultRequestHeaders.Add("X-Timezone", timezone);
		return new AfuntoApi(httpClient);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Testorg_{Guid.NewGuid()}" }, cancellationToken);
		return organization.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityWithTimeSlotAsync(
		AfuntoApi client,
		Guid orgId,
		string title,
		DateTimeOffset slotStart,
		CancellationToken cancellationToken,
		string? category = null)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Scheduled slots opportunity seeded for the date-availability calendar",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			Category = category,
			IsDraft = true,
		}, cancellationToken);

		await client.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = slotStart,
				EndDateTime = slotStart.AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		return opportunity;
	}
}
