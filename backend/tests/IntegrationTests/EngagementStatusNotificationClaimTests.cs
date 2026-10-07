using Application.Common.Exceptions;
using AwesomeAssertions;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using DomainOrganization = Domain.Organizations.Organization;
using DomainOrganizationId = Domain.Organizations.OrganizationId;

namespace IntegrationTests;

// The claim behind one-email-per-volunteer-and-opportunity (#2402): the first status
// email handler takes every unnotified engagement, its sibling events find nothing left.
[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EngagementStatusNotificationClaimTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task ClaimStatusNotificationsAsync_ClaimsEveryMatchingEngagementOnce(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var volunteerId = UserId.New();
		var (opportunity, engagements) = await SeedConfirmedEngagementsAsync(dbContext, volunteerId, count: 3, cancellationToken);

		var first = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);
		var second = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);

		first.Select(e => e.Id).Should().BeEquivalentTo(engagements.Select(e => e.Id));
		second.Should().BeEmpty("the sibling events of a bulk confirmation must not send a second email");
	}

	[Test]
	public async Task ClaimStatusNotificationsAsync_IgnoresOtherVolunteersAndOtherStatuses(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var volunteerId = UserId.New();
		var (opportunity, own) = await SeedConfirmedEngagementsAsync(dbContext, volunteerId, count: 1, cancellationToken);
		await SeedConfirmedEngagementsAsync(dbContext, UserId.New(), count: 1, cancellationToken);

		var cancelled = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Cancelled, DateTimeOffset.UtcNow, cancellationToken);
		var confirmed = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);

		cancelled.Should().BeEmpty();
		confirmed.Should().ContainSingle(e => e.Id == own[0].Id);
	}

	[Test]
	public async Task ReleaseStatusNotificationsAsync_MakesTheEngagementsClaimableAgain(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var volunteerId = UserId.New();
		var (opportunity, engagements) = await SeedConfirmedEngagementsAsync(dbContext, volunteerId, count: 2, cancellationToken);
		await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);

		await dbContext.ReleaseStatusNotificationsAsync([.. engagements.Select(e => e.Id)], cancellationToken);
		var reclaimed = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunity.Id, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);

		reclaimed.Should().HaveCount(2, "a failed send must leave the outbox retry something to email");
	}

	[Test]
	public async Task ClaimStatusNotificationsAsync_ClaimsAgainAfterTheStatusChanges(
		CancellationToken cancellationToken)
	{
		var volunteerId = UserId.New();
		VolunteerOpportunityId opportunityId;
		EngagementId engagementId;
		await using (var seedContext = fixture.CreateApplicationDbContext())
		{
			var (opportunity, engagements) = await SeedConfirmedEngagementsAsync(seedContext, volunteerId, count: 1, cancellationToken);
			opportunityId = opportunity.Id;
			engagementId = engagements[0].Id;
			await seedContext.ClaimStatusNotificationsAsync(
				volunteerId, opportunityId, EngagementStatus.Confirmed, DateTimeOffset.UtcNow, cancellationToken);
		}

		await using (var cancelContext = fixture.CreateApplicationDbContext())
		{
			var engagement = await cancelContext.Set<Engagement>().SingleAsync(e => e.Id == engagementId, cancellationToken);
			engagement.Cancel("Regen").ThrowIfFailure();
			await cancelContext.SaveChangesAsync(cancellationToken);
		}

		await using var dbContext = fixture.CreateApplicationDbContext();
		var cancelled = await dbContext.ClaimStatusNotificationsAsync(
			volunteerId, opportunityId, EngagementStatus.Cancelled, DateTimeOffset.UtcNow, cancellationToken);

		cancelled.Should().ContainSingle(e => e.Id == engagementId,
			"a cancellation after an already-announced confirmation is news the volunteer must still get");
	}

	[Test]
	public async Task MarkRemindersSentAsync_StampsOnlyEngagementsWithoutAReminder(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var (_, engagements) = await SeedConfirmedEngagementsAsync(dbContext, UserId.New(), count: 2, cancellationToken);
		var earlier = DateTimeOffset.UtcNow.AddDays(-1);
		await dbContext.Set<Engagement>()
			.Where(e => e.Id == engagements[1].Id)
			.ExecuteUpdateAsync(s => s.SetProperty(e => e.ReminderSentAt, earlier), cancellationToken);

		var ids = engagements.Select(e => e.Id).ToList();
		await dbContext.MarkRemindersSentAsync(ids, DateTimeOffset.UtcNow, cancellationToken);

		var reminders = await dbContext.Set<Engagement>()
			.AsNoTracking()
			.Where(e => ids.Contains(e.Id))
			.ToDictionaryAsync(e => e.Id, e => e.ReminderSentAt, cancellationToken);
		reminders[engagements[0].Id].Should().NotBeNull();
		reminders[engagements[1].Id].Should().BeCloseTo(earlier, TimeSpan.FromMilliseconds(1));
	}

	private static async Task<(VolunteerOpportunity Opportunity, List<Engagement> Engagements)> SeedConfirmedEngagementsAsync(
		ApplicationDbContext dbContext,
		UserId volunteerId,
		int count,
		CancellationToken cancellationToken)
	{
		var organization = DomainOrganization.Create(DomainOrganizationId.New(), $"ClaimTestOrg_{Guid.NewGuid()}").GetValueOrThrow();
		dbContext.Set<DomainOrganization>().Add(organization);

		var opportunity = VolunteerOpportunity.Create(
			organization.Id, "Tafel-Ausgabe", null, "Beschreibung", null, true, null,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, new NoOpPinGenerator(),
			status: OpportunityStatus.Draft).Value;
		dbContext.Set<VolunteerOpportunity>().Add(opportunity);

		var engagements = new List<Engagement>(count);
		for (var i = 0; i < count; i++)
		{
			var start = DateTimeOffset.UtcNow.AddDays(7 * (i + 1));
			var slot = opportunity.AddTimeSlot(start, start.AddHours(2), 10, DateTimeOffset.UtcNow).Value;
			var engagement = Engagement.CreateSlotSignUp(opportunity.Id, volunteerId, slot.Id, slot.StartDateTime, slot.EndDateTime);
			engagement.Confirm();
			dbContext.Set<Engagement>().Add(engagement);
			engagements.Add(engagement);
		}

		await dbContext.SaveChangesAsync(cancellationToken);
		return (opportunity, engagements);
	}

	private sealed class NoOpPinGenerator : IPinGenerator
	{
		public string GeneratePin() => "0000";
	}
}
