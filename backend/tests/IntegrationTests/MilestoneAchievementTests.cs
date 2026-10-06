using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class MilestoneAchievementTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task DedicatedBadge_IsAwarded_OnFifthConfirmation_EvenAfterAnEarlierConfirmedOpportunityWasDeleted(
		CancellationToken cancellationToken)
	{
		var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var volunteerClient = await fixture.CreateAuthenticatedClientAsync(username, password);
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var engagementIds = new List<Guid>();
		var opportunityIds = new List<Guid>();
		for (var i = 0; i < 5; i++)
		{
			var opportunityId = await CreateIndividualContactOpportunityAsync(
				olafClient, $"Milestone668-{i}", cancellationToken);
			opportunityIds.Add(opportunityId);

			var engagement = await volunteerClient.CreateEngagementAsync(
				opportunityId,
				new CreateEngagementRequest { Message = "Please let me help." },
				cancellationToken);
			engagementIds.Add(engagement.Id);
		}

		for (var i = 0; i < 4; i++)
			await olafClient.ConfirmEngagementAsync(engagementIds[i], cancellationToken);

		await olafClient.DeleteVolunteerOpportunityAsync(opportunityIds[0], cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagementIds[4], cancellationToken);

		var achievements = await volunteerClient.GetMyAchievementsAsync(cancellationToken);

		achievements.Select(a => a.Name).Should().Contain("Dedicated",
			"5 lifetime confirmations must award the Dedicated badge even though an "
			+ "earlier deletion left the live confirmed count at only 4");
	}

	private static async Task<Guid> CreateIndividualContactOpportunityAsync(
		AfuntoApi olafClient, string label, CancellationToken cancellationToken)
	{
		var suffix = Guid.NewGuid().ToString("N");

		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Milestone668 Org {suffix}" }, cancellationToken);

		var opportunity = await olafClient.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = $"Milestone668 {label} {suffix}",
				DescriptionDe = "Created by MilestoneAchievementTests",
				OrganizationId = org.Id.Value,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		return opportunity.Id;
	}
}
