using System.Net.Http.Headers;
using Application.Common.Exceptions;
using AwesomeAssertions;
using Domain.VolunteerOpportunities;
using Infrastructure.Persistence;

using DomainOrganizationId = Domain.Organizations.OrganizationId;
using DomainAddress = Domain.Common.Address;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetVolunteerOpportunitiesTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnEmptyPagedList_WhenNoneExist(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);
		await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Draft - never published",
			DescriptionDe = "Exists only to force a fresh output-cache read; see comment above.",
			OrganizationId = orgId,
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(0);
		result.Items.Should().BeEmpty();
		result.CurrentPage.Should().Be(1);
		result.PageCount.Should().Be(0);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnAll_WhenOpportunitiesExist(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 1", "Description 1", cancellationToken);
		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 2", "Description 2", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(2);
		result.Items.Should().HaveCount(2);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnCorrectPageSize_WhenPaginationIsApplied(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 1", "Description 1", cancellationToken);
		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 2", "Description 2", cancellationToken);
		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 3", "Description 3", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 2, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(2);
		result.PageCount.Should().Be(2);
		result.CurrentPage.Should().Be(1);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnRemainingItems_WhenRequestingLastPage(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 1", "Description 1", cancellationToken);
		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 2", "Description 2", cancellationToken);
		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity 3", "Description 3", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(2, 2, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(1);
		result.CurrentPage.Should().Be(2);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnOrderedByCreatedOnDescending(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var first = await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "First", "Created first", cancellationToken);
		var second = await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Second", "Created second", cancellationToken);
		var third = await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Third", "Created last", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var items = result.Items.ToList();
		items.Should().HaveCount(3);
		items[0].Id.Should().Be(third.Id);
		items[1].Id.Should().Be(second.Id);
		items[2].Id.Should().Be(first.Id);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnOrganizationName(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity", "Description", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single();
		item.OrganizationName.Should().Contain("Testorg_");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnAddressAndOccurrence(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "Opportunity", "Description", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single();
		item.Street.Should().Be("Sample Street");
		item.HouseNumber.Should().Be("1");
		item.ZipCode.Should().Be("12345");
		item.City.Should().Be("Berlin");
		item.Occurrence.Should().Be("OneTime");
		item.ParticipationType.Should().Be("ScheduledSlots");
		item.IsRemote.Should().BeFalse();
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnNextTimeSlotStartAndEnd_WhenTimeSlotExists(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var opportunity = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Opportunity with a slot",
			DescriptionDe = "Description",
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

		var start = DateTimeOffset.UtcNow.AddDays(7);
		var end = start.AddHours(2);
		await authenticatedClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = start,
				EndDateTime = end,
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await authenticatedClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());
		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single();
		item.NextTimeSlotStart.Should().BeCloseTo(start, TimeSpan.FromSeconds(1));
		item.NextTimeSlotEnd.Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnEarliestUpcomingTimeSlot_WhenMultipleSlotsExist(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var opportunity = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Opportunity with recurring slots",
			DescriptionDe = "Description",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "Recurring",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

		var earliestStart = DateTimeOffset.UtcNow.AddDays(3);
		await authenticatedClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = earliestStart,
				EndDateTime = earliestStart.AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "WEEKLY",
				RecurrenceCount = 3,
			},
			cancellationToken);

		await authenticatedClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());
		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single();
		item.NextTimeSlotStart.Should().BeCloseTo(earliestStart, TimeSpan.FromSeconds(1));
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnNullNextTimeSlot_WhenNoTimeSlotsExist(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Opportunity without slots",
			DescriptionDe = "Description",
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

		var sut = new AfuntoApi(fixture.CreateHttpClient());
		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single();
		item.NextTimeSlotStart.Should().BeNull();
		item.NextTimeSlotEnd.Should().BeNull();
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldExcludeOpportunitiesWhoseOnlyTimeSlotsHaveExpired(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var expired = await CreateOpportunityWithExpiredTimeSlotAsync(authenticatedClient, orgId, cancellationToken);

		var future = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Future opportunity",
			DescriptionDe = "Has an upcoming slot",
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
			future.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);
		await authenticatedClient.PublishVolunteerOpportunityAsync(future.Id, cancellationToken);

		var slotless = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Slotless opportunity",
			DescriptionDe = "Never has time slots",
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

		var sut = new AfuntoApi(fixture.CreateHttpClient());
		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var ids = result.Items.Select(i => i.Id).ToList();
		result.TotalItems.Should().Be(2);
		ids.Should().NotContain(expired.Id, "an opportunity whose only time slot has already ended must not surface publicly");
		ids.Should().Contain(future.Id);
		ids.Should().Contain(slotless.Id);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldStillAppear_WhenOnlySomeOfItsTimeSlotsHaveExpired(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var opportunity = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Mixed expiry opportunity",
			DescriptionDe = "One slot already ended, one is still upcoming",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "Recurring",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

		var futureStart = DateTimeOffset.UtcNow.AddDays(3);
		await authenticatedClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = futureStart,
				EndDateTime = futureStart.AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await AddExpiredTimeSlotDirectlyAsync(opportunity.Id, cancellationToken);

		await authenticatedClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());
		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, cancellationToken: cancellationToken);

		var item = result.Items.Single(i => i.Id == opportunity.Id);
		item.NextTimeSlotStart.Should().BeCloseTo(futureStart, TimeSpan.FromSeconds(1),
			"the still-upcoming slot must win over the already-expired one");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldIncludeSlotlessOpportunity_WhenDateFromFilterApplied(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotless = await CreateSlotlessOpportunityAsync(authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var nearSlotStart = DateTimeOffset.UtcNow.AddDays(2);
		var tooEarly = await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Too early for the filter", nearSlotStart, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10, dateFrom: DateTimeOffset.UtcNow.AddDays(5), cancellationToken: cancellationToken);

		var ids = result.Items.Select(i => i.Id).ToList();
		ids.Should().Contain(slotless.Id, "an opportunity with no time slots has no date to compare against and must not be hidden by a date filter");
		ids.Should().NotContain(tooEarly.Id, "its only time slot starts before the requested dateFrom");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldIncludeSlotlessOpportunity_WhenDateToFilterApplied(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotless = await CreateSlotlessOpportunityAsync(authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var farSlotStart = DateTimeOffset.UtcNow.AddDays(20);
		var tooLate = await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Too late for the filter", farSlotStart, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10, dateTo: DateTimeOffset.UtcNow.AddDays(5), cancellationToken: cancellationToken);

		var ids = result.Items.Select(i => i.Id).ToList();
		ids.Should().Contain(slotless.Id, "an opportunity with no time slots has no date to compare against and must not be hidden by a date filter");
		ids.Should().NotContain(tooLate.Id, "its only time slot starts after the requested dateTo");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterScheduledSlotsOpportunitiesByRange_WhileStillIncludingSlotlessOnes(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotless = await CreateSlotlessOpportunityAsync(authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var withinRange = await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Within range", DateTimeOffset.UtcNow.AddDays(5), cancellationToken);

		var outsideRange = await CreateOpportunityWithTimeSlotAsync(
			authenticatedClient, orgId, "Outside range", DateTimeOffset.UtcNow.AddDays(20), cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10,
			dateFrom: DateTimeOffset.UtcNow.AddDays(3),
			dateTo: DateTimeOffset.UtcNow.AddDays(7),
			cancellationToken: cancellationToken);

		var ids = result.Items.Select(i => i.Id).ToList();
		result.TotalItems.Should().Be(2);
		ids.Should().Contain(slotless.Id);
		ids.Should().Contain(withinRange.Id);
		ids.Should().NotContain(outsideRange.Id);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldExcludeSeries_WhoseSlotsStraddleTheRequestedWindow(
		CancellationToken cancellationToken)
	{
		// DateFrom and DateTo used to be two independent existence tests, so a series with
		// slots on either side of the window satisfied "has a slot >= from" and "has a slot
		// <= to" with two *different* slots and matched a window it has no slot in (#2319).
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var straddling = await CreateOpportunityWithTimeSlotsAsync(
			authenticatedClient,
			orgId,
			"Slots on both sides of the window",
			[DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(17)],
			cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10,
			dateFrom: DateTimeOffset.UtcNow.AddDays(8),
			dateTo: DateTimeOffset.UtcNow.AddDays(12),
			cancellationToken: cancellationToken);

		result.Items.Select(i => i.Id).Should().NotContain(
			straddling.Id,
			"neither of its two slots falls inside the requested window");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldExcludeSlotlessOpportunity_WhenTheWindowIsEntirelyInThePast(
		CancellationToken cancellationToken)
	{
		// A slot-less opportunity has no slot dates, but it is not dateless: it is only
		// listed at all while its ValidUntil is in the future, so it is on offer over
		// [now, ValidUntil]. Exempting it from every date filter let it match impossible
		// windows - including ones that ended years ago (#2319).
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotless = await CreateSlotlessOpportunityAsync(
			authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10,
			dateTo: DateTimeOffset.UtcNow.AddYears(-5),
			cancellationToken: cancellationToken);

		result.Items.Select(i => i.Id).Should().NotContain(slotless.Id);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldExcludeSlotlessOpportunity_WhenTheWindowStartsAfterItExpires(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		// CreateSlotlessOpportunityAsync sets ValidUntil 30 days out.
		var slotless = await CreateSlotlessOpportunityAsync(
			authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10,
			dateFrom: DateTimeOffset.UtcNow.AddDays(60),
			cancellationToken: cancellationToken);

		result.Items.Select(i => i.Id).Should().NotContain(
			slotless.Id,
			"it stops being on offer long before the requested window starts");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldIncludeSlotlessOpportunity_WhenTheWindowOverlapsItsValidity(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var slotless = await CreateSlotlessOpportunityAsync(
			authenticatedClient, orgId, "Express interest opportunity", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10,
			dateFrom: DateTimeOffset.UtcNow.AddDays(3),
			dateTo: DateTimeOffset.UtcNow.AddDays(7),
			cancellationToken: cancellationToken);

		result.Items.Select(i => i.Id).Should().Contain(
			slotless.Id,
			"it is still on offer across the requested window, it just has no slots to pin to a day");
	}

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldReturn403_WhenUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		var token = await fixture.GetAccessTokenAsync("vera", "vera123");
		var httpClient = fixture.CreateHttpClient();
		httpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
		var client = new AfuntoApi(httpClient);

		var act = () => client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Not allowed",
			DescriptionDe = "Vera cannot create opportunities",
			OrganizationId = Guid.NewGuid(),
			Street = "Test Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None"
		}, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldPersistAddressAndOccurrence(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var result = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Opportunity with address",
			DescriptionDe = "Test",
			OrganizationId = orgId,
			Street = "Main Street",
			HouseNumber = "42a",
			ZipCode = "54321",
			City = "Munich",
			Occurrence = "Recurring",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30)
		}, cancellationToken);

		result.Street.Should().Be("Main Street");
		result.HouseNumber.Should().Be("42a");
		result.ZipCode.Should().Be("54321");
		result.City.Should().Be("Munich");
		result.Occurrence.Should().Be("Recurring");
		result.ParticipationType.Should().Be("IndividualContact");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByIsRemote(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(authenticatedClient, orgId, "On-site task", "Description", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var onSite = await sut.GetVolunteerOpportunitiesAsync(1, 10, isRemote: false, cancellationToken: cancellationToken);
		var remote = await sut.GetVolunteerOpportunitiesAsync(1, 10, isRemote: true, cancellationToken: cancellationToken);

		onSite.TotalItems.Should().Be(1);
		remote.TotalItems.Should().Be(0);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnBadRequest_WhenRadiusIsNotPositive(
		CancellationToken cancellationToken)
	{
		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => sut.GetVolunteerOpportunitiesAsync(
			1, 10, centerLatitude: 52.5, centerLongitude: 13.4, radiusKm: 0, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnBadRequest_WhenOccurrenceIsInvalid(
		CancellationToken cancellationToken)
	{
		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => sut.GetVolunteerOpportunitiesAsync(
			1, 10, occurrence: "Bogus", cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnBadRequest_WhenParticipationTypeIsInvalid(
		CancellationToken cancellationToken)
	{
		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => sut.GetVolunteerOpportunitiesAsync(
			1, 10, participationType: "Nonsense", cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturnBadRequest_WhenCategoryIsInvalid(
		CancellationToken cancellationToken)
	{
		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => sut.GetVolunteerOpportunitiesAsync(
			1, 10, categories: ["NotACategory"], cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByOccurrenceAndParticipationTypeAndCategory_WhenValid(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		var opportunity = await authenticatedClient.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Matching opportunity",
			DescriptionDe = "Description",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			Category = "Environment",
			IsDraft = true,
		}, cancellationToken);

		await authenticatedClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await authenticatedClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var matching = await sut.GetVolunteerOpportunitiesAsync(
			1, 10, occurrence: "onetime", participationType: "ScheduledSlots", categories: ["environment"], cancellationToken: cancellationToken);
		var nonMatching = await sut.GetVolunteerOpportunitiesAsync(
			1, 10, occurrence: "Recurring", cancellationToken: cancellationToken);

		matching.TotalItems.Should().Be(1);
		nonMatching.TotalItems.Should().Be(0);
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByTag(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateOpportunityWithTagsAsync(
			authenticatedClient, orgId, "Beach Cleanup", ["outdoors", "environment"], cancellationToken);
		await CreateOpportunityWithTagsAsync(
			authenticatedClient, orgId, "Tutoring", ["education"], cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, tag: "environment", cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(1);
		result.Items.Single().TitleDe.Should().Be("Beach Cleanup");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByKeyword_MatchingTitleOrDescription(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(
			authenticatedClient, orgId, "Beach Cleanup", "Help remove litter from the shoreline", cancellationToken);
		await CreateVolunteerOpportunityAsync(
			authenticatedClient, orgId, "Reading Tutor", "Support kids with their reading skills", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var byTitle = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "cleanup", cancellationToken: cancellationToken);
		var byDescription = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "shoreline", cancellationToken: cancellationToken);

		byTitle.TotalItems.Should().Be(1);
		byTitle.Items.Single().TitleDe.Should().Be("Beach Cleanup");
		byDescription.TotalItems.Should().Be(1);
		byDescription.Items.Single().TitleDe.Should().Be("Beach Cleanup");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByKeyword_MatchingOrganizationName(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var matchingOrgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken, "Riverside Wildlife Rescue");
		var otherOrgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(
			authenticatedClient, matchingOrgId, "Weekend Shift", "General help needed", cancellationToken);
		await CreateVolunteerOpportunityAsync(
			authenticatedClient, otherOrgId, "Unrelated Task", "Nothing to do with wildlife", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "wildlife rescue", cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(1);
		result.Items.Single().TitleDe.Should().Be("Weekend Shift");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByKeyword_NormalizingGermanUmlautsAndEszett(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		await CreateVolunteerOpportunityAsync(
			authenticatedClient, orgId, "Fußball", "A friendly match in München", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var asciiTranscriptionOfEszett = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "Fussball", cancellationToken: cancellationToken);
		var asciiTranscriptionOfUmlaut = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "Muenchen", cancellationToken: cancellationToken);
		var nativeSpelling = await sut.GetVolunteerOpportunitiesAsync(1, 10, keyword: "Fußball", cancellationToken: cancellationToken);

		asciiTranscriptionOfEszett.TotalItems.Should().Be(1);
		asciiTranscriptionOfEszett.Items.Single().TitleDe.Should().Be("Fußball");
		asciiTranscriptionOfUmlaut.TotalItems.Should().Be(1);
		asciiTranscriptionOfUmlaut.Items.Single().TitleDe.Should().Be("Fußball");
		nativeSpelling.TotalItems.Should().Be(1);
		nativeSpelling.Items.Single().TitleDe.Should().Be("Fußball");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldFilterByRadius_AndOrderResultsByDistanceAscending(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);

		const double centerLat = 52.52;
		const double centerLon = 13.405;

		await using var dbContext = fixture.CreateApplicationDbContext();
		await SeedPublishedOpportunityWithCoordinatesAsync(
			dbContext, orgId, "Near Opportunity", centerLat + 0.01, centerLon, cancellationToken);
		await SeedPublishedOpportunityWithCoordinatesAsync(
			dbContext, orgId, "Mid Opportunity", centerLat + 0.05, centerLon, cancellationToken);
		await SeedPublishedOpportunityWithCoordinatesAsync(
			dbContext, orgId, "Far Opportunity", centerLat + 0.5, centerLon, cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var result = await sut.GetVolunteerOpportunitiesAsync(
			1, 10, centerLatitude: centerLat, centerLongitude: centerLon, radiusKm: 20, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(2, "only the near and mid opportunities fall within the 20km radius");
		result.Items.Select(i => i.TitleDe).Should().Equal(
			["Near Opportunity", "Mid Opportunity"],
			"radius search results must be ordered by distance ascending");
	}

	[Test]
	public async Task GetVolunteerOpportunities_ShouldReturn200_WhenPublishedOpportunitiesExist(
		CancellationToken cancellationToken)
	{
		var authenticatedClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(authenticatedClient, cancellationToken);
		await CreateVolunteerOpportunityAsync(
			authenticatedClient, orgId, "Translation probe", "Description", cancellationToken);

		var sut = new AfuntoApi(fixture.CreateHttpClient());

		var page = await sut.GetVolunteerOpportunitiesAsync(1, 1, cancellationToken: cancellationToken);

		page.Items.Should().ContainSingle();
		page.TotalItems.Should().Be(1);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken, string? namePrefix = null)
	{
		var uniqueName = $"{namePrefix ?? "Testorg"}_{Guid.NewGuid()}";
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return organization.Id.Value;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateVolunteerOpportunityAsync(
		AfuntoApi client, Guid orgId, string title, string description,
		CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = description,
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

		await client.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		return opportunity;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateSlotlessOpportunityAsync(
		AfuntoApi client, Guid orgId, string title, CancellationToken cancellationToken) =>
		await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
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

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityWithTimeSlotsAsync(
		AfuntoApi client, Guid orgId, string title, IReadOnlyList<DateTimeOffset> slotStarts, CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Scheduled slots opportunity with several time slots",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "Recurring",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		}, cancellationToken);

		foreach (var slotStart in slotStarts)
		{
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
		}

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		return opportunity;
	}

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityWithTimeSlotAsync(
		AfuntoApi client, Guid orgId, string title, DateTimeOffset slotStart, CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Scheduled slots opportunity with a single time slot",
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

	private async Task<CreateVolunteerOpportunityResponse> CreateOpportunityWithExpiredTimeSlotAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = "Expired opportunity",
			DescriptionDe = "Only has a time slot that already ended",
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

		await AddExpiredTimeSlotDirectlyAsync(opportunity.Id, cancellationToken);
		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		return opportunity;
	}

	private async Task AddExpiredTimeSlotDirectlyAsync(Guid opportunityId, CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var opportunityId_ = VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow();
		var aggregate = await dbContext.VolunteerOpportunities.FindAsync(opportunityId_, cancellationToken)
			?? throw new InvalidOperationException($"Seeded opportunity '{opportunityId}' not found.");

		var start = DateTimeOffset.UtcNow.AddDays(-7);
		aggregate.AddTimeSlot(start, start.AddHours(2), maxParticipants: 10, now: start.AddDays(-1)).GetValueOrThrow();

		await dbContext.SaveChangesAsync(cancellationToken);
	}

	private static Task<CreateVolunteerOpportunityResponse> CreateOpportunityWithTagsAsync(
		AfuntoApi client, Guid orgId, string title, string[] tags, CancellationToken cancellationToken) =>
		client.CreateVolunteerOpportunityAsync(new CreateVolunteerOpportunityRequest
		{
			TitleDe = title,
			DescriptionDe = "Description",
			OrganizationId = orgId,
			Street = "Sample Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "OneTime",
			ParticipationType = "IndividualContact",
			CheckInMethod = "None",
			Tags = tags,
			ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
		}, cancellationToken);

	private static async Task SeedPublishedOpportunityWithCoordinatesAsync(
		ApplicationDbContext dbContext, Guid orgId, string title, double latitude, double longitude,
		CancellationToken cancellationToken)
	{
		var address = DomainAddress.Create("Sample Street", "1", "12345", "Berlin").GetValueOrThrow()
			.WithCoordinates(latitude, longitude).GetValueOrThrow();

		var opportunity = VolunteerOpportunity.Create(
			DomainOrganizationId.Create(orgId).GetValueOrThrow(),
			title,
			null,
			"Description",
			null,
			isRemote: false,
			address,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			new NoOpPinGenerator(),
			status: OpportunityStatus.Published,
			validUntil: DateTimeOffset.UtcNow.AddDays(30)).GetValueOrThrow();

		dbContext.Set<VolunteerOpportunity>().Add(opportunity);
		await dbContext.SaveChangesAsync(cancellationToken);
	}

	private sealed class NoOpPinGenerator : IPinGenerator
	{
		public string GeneratePin() => "0000";
	}
}
