using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EngagementCheckInByCodeStatusCodeTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task CheckInEngagementByCode_Returns200_AndChecksIn_WhenCodeMatchesExactlyOne(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, engagementId) = await SeedConfirmedQrEngagementAsync(
			olaf, vera, "CheckInByCodeHappyPath", cancellationToken);

		var code = engagementId.ToString()[..8];
		var status = await olaf.CheckInEngagementByCodeAsync(
			opportunityId, new CheckInEngagementByCodeRequest { Code = code }, cancellationToken);

		status.Id.Should().Be(engagementId);
		status.Status.Should().Be("Confirmed");
	}

	[Test]
	public async Task CheckInEngagementByCode_Returns404_WhenNoEngagementMatchesCode(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, _) = await SeedConfirmedQrEngagementAsync(
			olaf, vera, "CheckInByCodeNotFound", cancellationToken);

		var act = () => olaf.CheckInEngagementByCodeAsync(
			opportunityId, new CheckInEngagementByCodeRequest { Code = "deadbeef" }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task CheckInEngagementByCode_Returns400_WhenCodeIsNotEightHexCharacters(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, _) = await SeedConfirmedQrEngagementAsync(
			olaf, vera, "CheckInByCodeInvalidFormat", cancellationToken);

		var act = () => olaf.CheckInEngagementByCodeAsync(
			opportunityId, new CheckInEngagementByCodeRequest { Code = "nope" }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CheckInEngagementByCode_Returns409_WhenOpportunityDoesNotUseQrCodeCheckIn(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var suffix = Guid.NewGuid().ToString("N");
		var org = await olaf.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"CheckInByCodeWrongMethod Org {suffix}" }, cancellationToken);
		var opportunity = await olaf.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = $"CheckInByCodeWrongMethod Opportunity {suffix}",
				DescriptionDe = "Created by EngagementCheckInByCodeStatusCodeTests.",
				OrganizationId = org.Id.Value,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "None",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		var act = () => olaf.CheckInEngagementByCodeAsync(
			opportunity.Id, new CheckInEngagementByCodeRequest { Code = "abcd1234" }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task CheckInEngagementByCode_Returns403_WhenRequestingUserIsNotAnOrganizerOfTheOrganization(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var (opportunityId, engagementId) = await SeedConfirmedQrEngagementAsync(
			olaf, vera, "CheckInByCodeNotOrganizer", cancellationToken);

		var act = () => vera.CheckInEngagementByCodeAsync(
			opportunityId, new CheckInEngagementByCodeRequest { Code = engagementId.ToString()[..8] }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CheckInEngagementByCode_Returns409_WhenTheCodeMatchesMoreThanOneEngagement(
		CancellationToken cancellationToken)
	{
		var olaf = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var vera = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var opportunityId = await CreateQrOpportunityAsync(olaf, "CheckInByCodeAmbiguous", cancellationToken);

		// EngagementId is a UUIDv7 (Domain/Engagements/EngagementId.cs): its first
		// 8 hex characters are a millisecond timestamp segment that only changes
		// every ~65 seconds, so two sign-ups created back to back like this -
		// exactly the burst-of-signups scenario this test models - reliably
		// share a code without any special setup. The two sign-ups need to be
		// from different volunteers: CreateEngagementCommandHandler rejects a
		// second sign-up from the same volunteer for the same IndividualContact
		// opportunity (Engagement.AlreadySignedUp), so olaf - who holds both
		// "organisator" and "user" roles - stands in as the second volunteer.
		var first = await vera.CreateEngagementAsync(
			opportunityId, new CreateEngagementRequest { Message = "I'd like to help!" }, cancellationToken);
		var second = await olaf.CreateEngagementAsync(
			opportunityId, new CreateEngagementRequest { Message = "Me too!" }, cancellationToken);
		await olaf.ConfirmEngagementAsync(first.Id, cancellationToken);
		await olaf.ConfirmEngagementAsync(second.Id, cancellationToken);

		var sharedCode = first.Id.ToString()[..8];
		sharedCode.Should().Be(second.Id.ToString()[..8],
			"this test relies on both engagements sharing a UUIDv7 timestamp segment - "
			+ "see the comment above");

		var act = () => olaf.CheckInEngagementByCodeAsync(
			opportunityId, new CheckInEngagementByCodeRequest { Code = sharedCode }, cancellationToken);

		var ex = await act.Should().ThrowAsync<ApiException>();
		ex.Which.StatusCode.Should().Be(409);
	}

	private async Task<(Guid OpportunityId, Guid EngagementId)> SeedConfirmedQrEngagementAsync(
		AfuntoApi olaf, AfuntoApi vera, string label, CancellationToken cancellationToken)
	{
		var opportunityId = await CreateQrOpportunityAsync(olaf, label, cancellationToken);
		var engagement = await vera.CreateEngagementAsync(
			opportunityId, new CreateEngagementRequest { Message = "I'd like to help!" }, cancellationToken);
		await olaf.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		return (opportunityId, engagement.Id);
	}

	private static async Task<Guid> CreateQrOpportunityAsync(
		AfuntoApi olaf, string label, CancellationToken cancellationToken)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var org = await olaf.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"{label} Org {suffix}" }, cancellationToken);

		var opportunity = await olaf.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = $"{label} Opportunity {suffix}",
				DescriptionDe = "Created by EngagementCheckInByCodeStatusCodeTests.",
				OrganizationId = org.Id.Value,
				IsRemote = true,
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = "QRCode",
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);

		return opportunity.Id;
	}
}
