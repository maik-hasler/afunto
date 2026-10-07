using AwesomeAssertions;
using Domain.Users;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class OrganizerNotificationDigestJobTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task ClaimBatchAsync_UnclaimedItem_ClaimsItAndStampsClaimedOnUtc(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var itemId = await SeedItemAsync(dbContext, cancellationToken);

		var claimed = await OrganizerNotificationDigestJob.ClaimBatchAsync(dbContext, 100, cancellationToken);

		claimed.Should().ContainSingle(i => i.Id == itemId);

		var claimedOnUtc = await dbContext.Set<PendingOrganizerDigestItem>()
			.AsNoTracking()
			.Where(i => i.Id == itemId)
			.Select(i => i.ClaimedOnUtc)
			.SingleAsync(cancellationToken);
		claimedOnUtc.Should().NotBeNull("claiming must stamp ClaimedOnUtc so a concurrent tick skips it");
	}

	[Test]
	public async Task ClaimBatchAsync_AlreadyDigested_DoesNotClaimAgain(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var itemId = await SeedItemAsync(dbContext, cancellationToken);
		await dbContext.Set<PendingOrganizerDigestItem>()
			.Where(i => i.Id == itemId)
			.ExecuteUpdateAsync(s => s.SetProperty(i => i.DigestSentOnUtc, DateTime.UtcNow), cancellationToken);

		var claimed = await OrganizerNotificationDigestJob.ClaimBatchAsync(dbContext, 100, cancellationToken);

		claimed.Should().BeEmpty("an item already digested must never be sent again");
	}

	[Test]
	public async Task ClaimBatchAsync_TwoConcurrentCallsAgainstTheSameItem_OnlyOneClaimsIt(
		CancellationToken cancellationToken)
	{
		await using var seedContext = fixture.CreateApplicationDbContext();
		var itemId = await SeedItemAsync(seedContext, cancellationToken);

		await using var contextA = fixture.CreateApplicationDbContext();
		await using var contextB = fixture.CreateApplicationDbContext();

		var results = await Task.WhenAll(
			OrganizerNotificationDigestJob.ClaimBatchAsync(contextA, 100, cancellationToken),
			OrganizerNotificationDigestJob.ClaimBatchAsync(contextB, 100, cancellationToken));

		var totalClaimed = results.Sum(r => r.Count);
		totalClaimed.Should().Be(1, "exactly one of the two concurrent ticks should have won the claim");

		var stillMatching = results.SelectMany(r => r).Count(i => i.Id == itemId);
		stillMatching.Should().Be(1);
	}

	[Test]
	public async Task ClaimBatchAsync_FreshlyClaimedItem_IsNotReclaimedByASecondTick(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var itemId = await SeedItemAsync(dbContext, cancellationToken);
		await dbContext.Set<PendingOrganizerDigestItem>()
			.Where(i => i.Id == itemId)
			.ExecuteUpdateAsync(s => s.SetProperty(i => i.ClaimedOnUtc, DateTime.UtcNow.AddMinutes(-5)), cancellationToken);

		var claimed = await OrganizerNotificationDigestJob.ClaimBatchAsync(dbContext, 100, cancellationToken);

		claimed.Should().BeEmpty(
			"a batch claimed 5 minutes ago is still well within the staleness window and may still be mid-processing");
	}

	[Test]
	public async Task ClaimBatchAsync_StaleClaimedItem_IsReclaimed(
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var itemId = await SeedItemAsync(dbContext, cancellationToken);
		await dbContext.Set<PendingOrganizerDigestItem>()
			.Where(i => i.Id == itemId)
			.ExecuteUpdateAsync(s => s.SetProperty(i => i.ClaimedOnUtc, DateTime.UtcNow.AddHours(-2)), cancellationToken);

		var claimed = await OrganizerNotificationDigestJob.ClaimBatchAsync(dbContext, 100, cancellationToken);

		claimed.Should().ContainSingle(i => i.Id == itemId,
			"a claim from 2 hours ago outlived the staleness window, implying the tick that claimed it crashed");
	}

	private static async Task<Guid> SeedItemAsync(
		ApplicationDbContext dbContext,
		CancellationToken cancellationToken)
	{
		var item = PendingOrganizerDigestItem.Create(
			Guid.NewGuid(), Guid.NewGuid(), "Beach Cleanup", "Vera Volunteer", EmailNotificationType.NewSignUp, DateTime.UtcNow);
		dbContext.Set<PendingOrganizerDigestItem>().Add(item);
		await dbContext.SaveChangesAsync(cancellationToken);
		return item.Id;
	}
}
