using Application.Common.Exceptions;
using AwesomeAssertions;
using Domain.VolunteerOpportunities;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class GetPublicOrganizationProfileTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldCarryNextTimeSlotStart_ForASlotBasedOpportunity(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var slotStart = DateTimeOffset.UtcNow.AddDays(7);
		await PublishSlotBasedOpportunityAsync(
			client, organizationId, "Slot based", slotStart, cancellationToken);

		var profile = await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken);

		var summary = profile.OpenOpportunities.Should().ContainSingle().Which;

		summary.NextTimeSlotStart.Should().NotBeNull();
		summary.NextTimeSlotStart.Should().BeCloseTo(slotStart, TimeSpan.FromMinutes(1));
		summary.ValidUntil.Should().BeNull();
	}

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldCarryValidUntil_ForAnInterestBasedOpportunity(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var deadline = DateTimeOffset.UtcNow.AddDays(30);
		await PublishInterestBasedOpportunityAsync(
			client, organizationId, "Interest based", deadline, cancellationToken);

		var profile = await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken);

		var summary = profile.OpenOpportunities.Should().ContainSingle().Which;

		summary.ValidUntil.Should().NotBeNull();
		summary.ValidUntil.Should().BeCloseTo(deadline, TimeSpan.FromMinutes(1));
		summary.NextTimeSlotStart.Should().BeNull();
	}

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldCarryCategoryAndCapacity(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		await PublishSlotBasedOpportunityAsync(
			client, organizationId, "Categorised", DateTimeOffset.UtcNow.AddDays(7),
			cancellationToken, category: "Environment", maxParticipants: 10);

		var profile = await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken);

		var summary = profile.OpenOpportunities.Should().ContainSingle().Which;

		summary.Category.Should().Be("Environment");
		summary.TotalMaxParticipants.Should().Be(10);
		summary.CurrentParticipantCount.Should().Be(0);
	}

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldExcludeOpportunity_WhenIndividualContactDeadlineHasPassed(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		var opportunity = await PublishInterestBasedOpportunityAsync(
			client, organizationId, "Expired interest based", DateTimeOffset.UtcNow.AddDays(30), cancellationToken);
		await SetExpiredValidUntilDirectlyAsync(opportunity.Id, cancellationToken);

		var profile = await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken);

		profile.OpenOpportunities.Should().BeEmpty();
	}

	[Test]
	public async Task GetPublicOrganizationProfile_ShouldExcludeEndedTimeSlots_FromTheAdvertisedCapacity(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var organizationId = await CreateOrganizationAsync(client, cancellationToken);
		await PublishSlotBasedOpportunityAsync(
			client, organizationId, "Half expired", DateTimeOffset.UtcNow.AddDays(7),
			cancellationToken, maxParticipants: 10);
		var opportunityId = (await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken))
			.OpenOpportunities.Should().ContainSingle().Which.Id;
		await AddEndedTimeSlotDirectlyAsync(opportunityId, cancellationToken);

		var profile = await new AfuntoApi(fixture.CreateHttpClient())
			.GetPublicOrganizationProfileAsync(organizationId, cancellationToken);

		// The ended slot's 10 seats can never be booked, so advertising 20 would promise
		// capacity that does not exist (afunto#2318).
		profile.OpenOpportunities.Should().ContainSingle().Which
			.TotalMaxParticipants.Should().Be(10);
	}

	private async Task AddEndedTimeSlotDirectlyAsync(Guid opportunityId, CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var opportunityId_ = VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow();
		var aggregate = await dbContext.VolunteerOpportunities.FindAsync(opportunityId_, cancellationToken)
			?? throw new InvalidOperationException($"Seeded opportunity '{opportunityId}' not found.");

		var start = DateTimeOffset.UtcNow.AddDays(-7);
		aggregate.AddTimeSlot(start, start.AddHours(2), maxParticipants: 10, now: start.AddDays(-1))
			.GetValueOrThrow();

		await dbContext.SaveChangesAsync(cancellationToken);
	}

	private async Task SetExpiredValidUntilDirectlyAsync(Guid opportunityId, CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var opportunityId_ = VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow();
		var aggregate = await dbContext.VolunteerOpportunities.FindAsync(opportunityId_, cancellationToken)
			?? throw new InvalidOperationException($"Seeded opportunity '{opportunityId}' not found.");

		var farPast = DateTimeOffset.UtcNow.AddDays(-30);
		aggregate.SetValidUntil(farPast.AddDays(1), now: farPast).ThrowIfFailure();

		await dbContext.SaveChangesAsync(cancellationToken);
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var organization = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Card contract {Guid.NewGuid():N}" },
			cancellationToken);
		return organization.Id.Value;
	}

	private static async Task PublishSlotBasedOpportunityAsync(
		AfuntoApi client,
		Guid organizationId,
		string title,
		DateTimeOffset slotStart,
		CancellationToken cancellationToken,
		string? category = null,
		int maxParticipants = 10)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = title,
				DescriptionDe = "Coverage for the public profile's opportunity summary.",
				OrganizationId = organizationId,
				IsRemote = true,
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
				MaxParticipants = maxParticipants,
				RecurrenceCount = 1,
			},
			cancellationToken);

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);
	}

	private static async Task<CreateVolunteerOpportunityResponse> PublishInterestBasedOpportunityAsync(
		AfuntoApi client,
		Guid organizationId,
		string title,
		DateTimeOffset validUntil,
		CancellationToken cancellationToken)
	{
		var opportunity = await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = title,
				DescriptionDe = "Coverage for the public profile's opportunity summary.",
				OrganizationId = organizationId,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = validUntil,
				IsDraft = true,
			}, cancellationToken);

		await client.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);
		return opportunity;
	}
}
