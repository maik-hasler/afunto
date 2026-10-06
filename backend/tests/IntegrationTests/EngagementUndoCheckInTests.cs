using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EngagementUndoCheckInTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task UndoCheckIn_ClearsTheFlagButKeepsConfirmed_WhenCalledByOrganizer(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, _) = await CreateOpportunityAsync(
			olaf, "UndoCheckInHappyPath", cancellationToken);
		var engagementId = await CreateAndConfirmEngagementAsync(
			vera, olaf, opportunityId, cancellationToken);

		await olaf.CheckInEngagementAsync(opportunityId, engagementId, cancellationToken);

		var undone = await olaf.UndoCheckInEngagementAsync(engagementId, cancellationToken);

		undone.Status.Should().Be("Confirmed",
			"undoing a check-in must not change the engagement's Confirmed status, "
			+ "only the check-in flag");
		var engagement = await GetEngagementAsync(olaf, opportunityId, engagementId, cancellationToken);
		engagement.IsCheckedIn.Should().BeFalse();
	}

	[Test]
	public async Task UndoCheckIn_Returns409_WhenEngagementIsNotCheckedIn(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, _) = await CreateOpportunityAsync(
			olaf, "UndoCheckInNotCheckedIn", cancellationToken);
		var engagementId = await CreateAndConfirmEngagementAsync(
			vera, olaf, opportunityId, cancellationToken);

		var failure = await CaptureFailureAsync(
			() => olaf.UndoCheckInEngagementAsync(engagementId, cancellationToken));

		failure.StatusCode.Should().Be(409);
		ErrorCodeOf(failure).Should().Be("Engagement.CheckInNotActive");
	}

	[Test]
	public async Task UndoCheckIn_Returns409_WhenEngagementIsTerminated(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, _) = await CreateOpportunityAsync(
			olaf, "UndoCheckInTerminated", cancellationToken);
		var engagementId = await CreateAndConfirmEngagementAsync(
			vera, olaf, opportunityId, cancellationToken);

		await olaf.CheckInEngagementAsync(opportunityId, engagementId, cancellationToken);
		await olaf.CancelEngagementAsync(engagementId, body: null, cancellationToken);

		var failure = await CaptureFailureAsync(
			() => olaf.UndoCheckInEngagementAsync(engagementId, cancellationToken));

		failure.StatusCode.Should().Be(409);
		ErrorCodeOf(failure).Should().Be("Engagement.AlreadyTerminated");
	}

	[Test]
	public async Task UndoCheckIn_Returns403_AndLeavesTheFlagSet_WhenCallerIsNotTheOrganizer(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var admin = await fixture.CreateAuthenticatedClientAsync("admin", "admin123");

		var (opportunityId, _) = await CreateOpportunityAsync(
			olaf, "UndoCheckInForbidden", cancellationToken);
		var engagementId = await CreateAndConfirmEngagementAsync(
			vera, olaf, opportunityId, cancellationToken);

		await olaf.CheckInEngagementAsync(opportunityId, engagementId, cancellationToken);

		var failure = await CaptureFailureAsync(
			() => admin.UndoCheckInEngagementAsync(engagementId, cancellationToken));

		failure.StatusCode.Should().Be(403);
		var engagement = await GetEngagementAsync(olaf, opportunityId, engagementId, cancellationToken);
		engagement.IsCheckedIn.Should().BeTrue(
			"a rejected undo must leave the engagement exactly as it found it");
	}

	private static async Task<(Guid OpportunityId, Guid OrganizationId)> CreateOpportunityAsync(
		AfuntoApi olaf, string label, CancellationToken cancellationToken)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var org = await olaf.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"{label} Org {suffix}" }, cancellationToken);

		var opportunity = await olaf.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = $"{label} Opportunity {suffix}",
				DescriptionDe = "Created by EngagementUndoCheckInTests.",
				OrganizationId = org.Id.Value,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		return (opportunity.Id, org.Id.Value);
	}

	private static async Task<Guid> CreateAndConfirmEngagementAsync(
		AfuntoApi vera,
		AfuntoApi olaf,
		Guid opportunityId,
		CancellationToken cancellationToken)
	{
		var engagement = await vera.CreateEngagementAsync(
			opportunityId,
			new CreateEngagementRequest { Message = "Undo check-in test signup" },
			cancellationToken);
		await olaf.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		return engagement.Id;
	}

	private static async Task<EngagementSummary> GetEngagementAsync(
		AfuntoApi olaf,
		Guid opportunityId,
		Guid engagementId,
		CancellationToken cancellationToken)
	{
		var page = await olaf.GetEngagementsAsync(
			opportunityId, pageNumber: 1, pageSize: 10, status: null, timeSlotId: null,
			search: null, cancellationToken);

		return page.Items.FirstOrDefault(e => e.Id == engagementId)
			?? throw new InvalidOperationException(
				$"Engagement '{engagementId}' not found in GetEngagements response.");
	}

	private static async Task<ApiException<ProblemDetails>> CaptureFailureAsync(Func<Task> act)
	{
		try
		{
			await act();
		}
		catch (ApiException<ProblemDetails> ex)
		{
			return ex;
		}

		throw new Exception("Expected the undo-check-in call to fail, but it succeeded.");
	}

	private static string? ErrorCodeOf(ApiException<ProblemDetails> ex)
	{
		ex.Result.AdditionalProperties.Should().ContainKey("errorCode",
			"a Result-pattern failure must carry an errorCode in its ProblemDetails");
		return ex.Result.AdditionalProperties["errorCode"]?.ToString();
	}
}
