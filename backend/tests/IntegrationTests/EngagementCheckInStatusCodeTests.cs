using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EngagementCheckInStatusCodeTests(IntegrationTestFixture fixture)
{
	private const string Pin = "482170";

	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task CheckInEngagement_Returns400_WhenEngagementIsNotConfirmed(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var opportunityId = await CreateOpportunityAsync(
			olaf, "CheckInStatusCode", checkInPin: null, cancellationToken);
		var engagement = await vera.CreateEngagementAsync(
			opportunityId,
			new CreateEngagementRequest { Message = "I'd like to help!" },
			cancellationToken);

		var act = () => olaf.CheckInEngagementAsync(opportunityId, engagement.Id, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(400,
			"Engagement.CheckIn() on a Pending (not yet Confirmed) engagement must return 400, "
			+ "matching pre-refactor behaviour");
	}

	[Test]
	public async Task CheckInWithPin_Returns400_WhenCheckingInSomeoneElsesEngagement(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var engagementId = await SeedConfirmedPinEngagementAsync(
			olaf, vera, "CheckInPinOwner", cancellationToken);

		var act = () => olaf.CheckInWithPinAsync(
			engagementId, new CheckInWithPinRequest { Pin = Pin }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(400,
			"CheckInWithPin on someone else's engagement must return 400, matching "
			+ "pre-refactor behaviour");
	}

	[Test]
	public async Task CheckInWithPin_ReturnsIdenticalNotOwnerError_RegardlessOfPinCorrectness_ForNonOwner(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var engagementId = await SeedConfirmedPinEngagementAsync(
			olaf, vera, "CheckInPinOracle", cancellationToken);

		var wrongPin = await CaptureFailureAsync(() => olaf.CheckInWithPinAsync(
			engagementId, new CheckInWithPinRequest { Pin = "000000" }, cancellationToken));
		var correctPin = await CaptureFailureAsync(() => olaf.CheckInWithPinAsync(
			engagementId, new CheckInWithPinRequest { Pin = Pin }, cancellationToken));

		wrongPin.StatusCode.Should().Be(400);
		correctPin.StatusCode.Should().Be(400);

		ErrorCodeOf(wrongPin).Should().Be("Engagement.NotOwner");
		ErrorCodeOf(correctPin).Should().Be("Engagement.NotOwner",
			"a non-owner must never learn whether their PIN guess was correct");
	}

	[Test]
	public async Task CheckInWithPin_Returns403_AfterTooManyFailedAttempts(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var engagementId = await SeedConfirmedPinEngagementAsync(
			olaf, vera, "CheckInPinLockout", cancellationToken);

		for (var attempt = 0; attempt < 5; attempt++)
		{
			var wrong = await CaptureFailureAsync(() => vera.CheckInWithPinAsync(
				engagementId, new CheckInWithPinRequest { Pin = "000000" }, cancellationToken));
			wrong.StatusCode.Should().Be(400);
		}

		var lockedOut = await CaptureFailureAsync(() => vera.CheckInWithPinAsync(
			engagementId, new CheckInWithPinRequest { Pin = Pin }, cancellationToken));

		lockedOut.StatusCode.Should().Be(403,
			"the correct PIN must still be rejected once the per-engagement lockout has tripped");
	}

	private async Task<Guid> SeedConfirmedPinEngagementAsync(
		AfuntoApi olaf, AfuntoApi vera, string label,
		CancellationToken cancellationToken)
	{
		var opportunityId = await CreateOpportunityAsync(olaf, label, Pin, cancellationToken);
		var engagement = await vera.CreateEngagementAsync(
			opportunityId,
			new CreateEngagementRequest { Message = "I'd like to help!" },
			cancellationToken);
		await olaf.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		return engagement.Id;
	}

	private static async Task<Guid> CreateOpportunityAsync(
		AfuntoApi olaf, string label, string? checkInPin,
		CancellationToken cancellationToken)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var org = await olaf.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"{label} Org {suffix}" }, cancellationToken);

		var opportunity = await olaf.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = $"{label} Opportunity {suffix}",
				DescriptionDe = "Created by EngagementCheckInStatusCodeTests.",
				OrganizationId = org.Id.Value,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = checkInPin is null ? "None" : "PINCode",
				CheckInPin = checkInPin,
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		return opportunity.Id;
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

		throw new Exception("Expected the check-in call to fail, but it succeeded.");
	}

	private static string? ErrorCodeOf(ApiException<ProblemDetails> ex)
	{
		ex.Result.AdditionalProperties.Should().ContainKey("errorCode",
			"a Result-pattern failure must carry an errorCode in its ProblemDetails");
		return ex.Result.AdditionalProperties["errorCode"]?.ToString();
	}
}
