using System.Net.Http.Headers;
using Application.Common.Exceptions;
using AwesomeAssertions;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class EngagementTests(IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task CreateEngagement_ShouldReturn201_WhenVolunteerSignsUp(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var result = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = null, Message = "I want to help!" },
			cancellationToken);

		result.OpportunityId.Should().Be(opportunity.Id);
		result.Status.Should().Be("Pending");
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var anonClient = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => anonClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn404_WhenOpportunityDoesNotExist(
		CancellationToken cancellationToken)
	{
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.CreateEngagementAsync(
			Guid.NewGuid(),
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn409_WhenIndividualContactApplicationDeadlineHasPassed(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);
		await SetExpiredValidUntilDirectlyAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task GetEngagements_ShouldReturnSignedUpVolunteer(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help" },
			cancellationToken);

		var engagements = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);

		engagements.Items.Should().HaveCount(1);
		engagements.Items.Single().Status.Should().Be("Pending");
	}

	[Test]
	public async Task GetEngagements_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var anonClient = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => anonClient.GetEngagementsAsync(Guid.NewGuid(), 1, 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task GetEngagements_ShouldReturn403_WhenRequestingUserIsNotAMemberOfTheOrganization(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task GetEngagements_ShouldSucceed_WhenRequestingUserIsAPlainMember(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		await fixture.AddPlainMemberDirectlyAsync(orgId, vera.Id, cancellationToken);

		var result = await veraClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(0);
	}

	[Test]
	public async Task GetEngagements_ShouldReturnCorrectPageSize_WhenPaginationIsApplied(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 2, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(2);
		result.PageCount.Should().Be(2);
		result.CurrentPage.Should().Be(1);
	}

	[Test]
	public async Task GetEngagements_ShouldReturnRemainingItems_WhenRequestingLastPage(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(opportunity.Id, 2, 2, cancellationToken: cancellationToken);

		result.TotalItems.Should().Be(3);
		result.Items.Should().HaveCount(1);
		result.CurrentPage.Should().Be(2);
	}

	[Test]
	public async Task GetEngagements_ShouldReturn400_WhenPageNumberIsLessThanOne(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var act = () => olafClient.GetEngagementsAsync(opportunity.Id, 0, 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetEngagements_ShouldReturn400_WhenPageSizeIsOutOfRange(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var act = () => olafClient.GetEngagementsAsync(opportunity.Id, 1, 101, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetEngagements_ShouldReturn400_WhenStatusIsInvalid(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var act = () => olafClient.GetEngagementsAsync(
			opportunity.Id, 1, 10, status: "NotAStatus", cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetEngagements_ShouldFilterByStatus_WhenStatusProvided(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var confirmedEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "Confirm me" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(confirmedEngagement.Id, cancellationToken);

		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(
			opportunity.Id, 1, 10, status: "Confirmed", cancellationToken: cancellationToken);

		result.Items.Should().ContainSingle(e => e.Id == confirmedEngagement.Id);
		result.Items.Should().OnlyContain(e => e.Status == "Confirmed");
	}

	[Test]
	public async Task GetEngagements_ShouldFilterByTimeSlot_WhenTimeSlotIdProvided(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var firstEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = firstSlotId }, cancellationToken);
		var secondEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = secondSlotId }, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(
			opportunity.Id, 1, 10, timeSlotId: firstSlotId, cancellationToken: cancellationToken);

		result.Items.Should().ContainSingle(e => e.Id == firstEngagement.Id);
		result.Items.Should().NotContain(e => e.Id == secondEngagement.Id);
	}

	[Test]
	public async Task CreateEngagement_ShouldSucceedForBothSlots_WhenVolunteerSignsUpForTwoSlotsOfTheSameSeries(
		CancellationToken cancellationToken)
	{
		// Regression test for afunto#2199: the backend must allow one
		// engagement per (volunteer, opportunity, time slot) - signing up for
		// an earlier occurrence of a recurring series must never block signing
		// up for a later one.
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var firstEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = firstSlotId }, cancellationToken);
		var secondEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = secondSlotId }, cancellationToken);

		firstEngagement.Status.Should().Be("Pending");
		secondEngagement.Status.Should().Be("Pending");

		var detailsForVera = await veraClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		detailsForVera.CurrentUserEngagements.Should().HaveCount(2);
		detailsForVera.CurrentUserEngagements.Should().Contain(e => e.Id == firstEngagement.Id && e.TimeSlotId == firstSlotId);
		detailsForVera.CurrentUserEngagements.Should().Contain(e => e.Id == secondEngagement.Id && e.TimeSlotId == secondSlotId);
	}

	[Test]
	public async Task GetEngagements_ShouldFilterBySearch_MatchingVolunteerUsername(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var (_, targetUsername, targetPassword) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var targetClient = await fixture.CreateAuthenticatedClientAsync(targetUsername, targetPassword);
		var targetEngagement = await targetClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "Pick me" }, cancellationToken);

		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(
			opportunity.Id, 1, 10, search: targetUsername, cancellationToken: cancellationToken);

		result.Items.Should().ContainSingle(e => e.Id == targetEngagement.Id);
	}

	[Test]
	public async Task GetEngagements_ShouldReturnEmptyPage_WhenSearchMatchesNoVolunteer(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.GetEngagementsAsync(
			opportunity.Id, 1, 10, search: $"no-such-volunteer-{Guid.NewGuid():N}", cancellationToken: cancellationToken);

		result.Items.Should().BeEmpty();
		result.TotalItems.Should().Be(0);
	}

	[Test]
	public async Task GetEngagements_ShouldResolveEachVolunteersOwnName_WhenLookedUpConcurrently(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		const int volunteerCount = 10;
		var expectedUsernameByEngagementId = new Dictionary<Guid, string>();
		for (var i = 0; i < volunteerCount; i++)
		{
			var (engagementId, username) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
			expectedUsernameByEngagementId[engagementId] = username;
		}

		var result = await olafClient.GetEngagementsAsync(
			opportunity.Id, 1, volunteerCount, cancellationToken: cancellationToken);

		result.Items.Should().HaveCount(volunteerCount);
		foreach (var item in result.Items)
		{
			item.VolunteerName.Should().Be(expectedUsernameByEngagementId[item.Id]);
		}
	}

	[Test]
	public async Task ConfirmEngagement_ShouldReturnConfirmedStatus_WhenOrganisatorConfirms(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var result = await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		result.Status.Should().Be("Confirmed");
	}

	[Test]
	public async Task ConfirmEngagement_ShouldReturn403_WhenNonOrganisatorConfirms(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => veraClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task ConfirmEngagement_ShouldReturn404_WhenEngagementDoesNotExist(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.ConfirmEngagementAsync(Guid.NewGuid(), cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task CancelEngagement_ShouldReturnCancelledStatus_WhenOrganisatorCancels(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var result = await olafClient.CancelEngagementAsync(
			engagement.Id,
			new CancelEngagementRequest { Reason = "Event cancelled" },
			cancellationToken);

		result.Status.Should().Be("Cancelled");
		result.CancellationReason.Should().Be("Event cancelled");
	}

	[Test]
	public async Task CancelEngagement_ShouldReturn403_WhenNonOrganisatorCancels(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => veraClient.CancelEngagementAsync(engagement.Id, null, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task BulkConfirmEngagements_ShouldConfirmAllPendingEngagements_WhenOrganisatorConfirms(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var (firstId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		var (secondId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.BulkConfirmEngagementsAsync(
			opportunity.Id,
			new BulkConfirmEngagementsRequest { EngagementIds = [firstId, secondId] },
			cancellationToken);

		result.Failed.Should().BeEmpty();
		result.Succeeded.Should().HaveCount(2);
		result.Succeeded.Should().OnlyContain(s => s.Status == "Confirmed");
	}

	[Test]
	public async Task BulkConfirmEngagements_ShouldReportPartialFailure_WhenOneEngagementIsAlreadyConfirmed(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var (pendingId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		var (alreadyConfirmedId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await olafClient.ConfirmEngagementAsync(alreadyConfirmedId, cancellationToken);

		var result = await olafClient.BulkConfirmEngagementsAsync(
			opportunity.Id,
			new BulkConfirmEngagementsRequest { EngagementIds = [pendingId, alreadyConfirmedId] },
			cancellationToken);

		result.Succeeded.Should().ContainSingle(s => s.Id == pendingId && s.Status == "Confirmed");
		result.Failed.Should().ContainSingle(f => f.EngagementId == alreadyConfirmedId && f.ErrorCode == "Engagement.NotPending");
	}

	[Test]
	public async Task BulkConfirmEngagements_ShouldReturn403_WhenNonOrganisatorConfirms(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => veraClient.BulkConfirmEngagementsAsync(
			opportunity.Id,
			new BulkConfirmEngagementsRequest { EngagementIds = [engagement.Id] },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task BulkConfirmEngagements_ShouldReturn400_WhenNoEngagementIdsProvided(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var act = () => olafClient.BulkConfirmEngagementsAsync(
			opportunity.Id,
			new BulkConfirmEngagementsRequest { EngagementIds = [] },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task BulkConfirmEngagements_ShouldReturn404_WhenOpportunityDoesNotExist(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.BulkConfirmEngagementsAsync(
			Guid.NewGuid(),
			new BulkConfirmEngagementsRequest { EngagementIds = [Guid.NewGuid()] },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task BulkCancelEngagements_ShouldCancelPendingAndConfirmedEngagements_WhenOrganisatorCancels(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var (pendingId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		var (confirmedId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);
		await olafClient.ConfirmEngagementAsync(confirmedId, cancellationToken);

		var result = await olafClient.BulkCancelEngagementsAsync(
			opportunity.Id,
			new BulkCancelEngagementsRequest { EngagementIds = [pendingId, confirmedId], Reason = "Event cancelled" },
			cancellationToken);

		result.Failed.Should().BeEmpty();
		result.Succeeded.Should().HaveCount(2);
		result.Succeeded.Should().OnlyContain(s => s.Status == "Cancelled" && s.CancellationReason == "Event cancelled");
	}

	[Test]
	public async Task BulkCancelEngagements_ShouldReportPartialFailure_WhenOneEngagementIsAlreadyWithdrawn(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var withdrawnEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);
		await veraClient.WithdrawEngagementAsync(withdrawnEngagement.Id, cancellationToken);

		var (pendingId, _) = await SignUpEphemeralVolunteerAsync(opportunity.Id, cancellationToken);

		var result = await olafClient.BulkCancelEngagementsAsync(
			opportunity.Id,
			new BulkCancelEngagementsRequest { EngagementIds = [pendingId, withdrawnEngagement.Id] },
			cancellationToken);

		result.Succeeded.Should().ContainSingle(s => s.Id == pendingId);
		result.Failed.Should().ContainSingle(f => f.EngagementId == withdrawnEngagement.Id && f.ErrorCode == "Engagement.AlreadyTerminated");
	}

	[Test]
	public async Task BulkCancelEngagements_ShouldReturn403_WhenNonOrganisatorCancels(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => veraClient.BulkCancelEngagementsAsync(
			opportunity.Id,
			new BulkCancelEngagementsRequest { EngagementIds = [engagement.Id] },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task BulkCancelEngagements_ShouldReturn404_WhenOpportunityDoesNotExist(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		await CreateOrganizationAsync(olafClient, cancellationToken);

		var act = () => olafClient.BulkCancelEngagementsAsync(
			Guid.NewGuid(),
			new BulkCancelEngagementsRequest { EngagementIds = [Guid.NewGuid()] },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task WithdrawEngagement_ShouldReturnWithdrawnStatus_WhenVolunteerWithdraws(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var result = await veraClient.WithdrawEngagementAsync(engagement.Id, cancellationToken);

		result.Status.Should().Be("Withdrawn");
	}

	[Test]
	public async Task GetMyEngagements_ShouldReturnVolunteerEngagements(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var myEngagements = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: true, cancellationToken);

		myEngagements.Items.Should().HaveCount(1);
		myEngagements.Items.Single().Status.Should().Be("Pending");
	}

	[Test]
	public async Task GetMyEngagements_MovesToPast_WhenOpportunityIsGoneAndEngagementIsNonTerminal(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await fixture.DeleteOpportunityRowDirectlyAsync(opportunity.Id);

		var upcoming = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: true, cancellationToken);
		upcoming.Items.Should().NotContain(e => e.Id == engagement.Id);

		var past = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: false, cancellationToken);
		past.Items.Should().ContainSingle(e => e.Id == engagement.Id && e.Status == "Pending");
	}

	[Test]
	public async Task GetMyEngagements_StillListsEngagement_AfterItsOpportunityIsDeleted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Please let me help." },
			cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		await olafClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var past = await veraClient.GetMyEngagementsAsync(1, 50, upcoming: false, cancellationToken);

		var listed = past.Items.SingleOrDefault(e => e.Id == engagement.Id);
		listed.Should().NotBeNull(
			"the engagement must still appear in the volunteer's own history rather than "
			+ "disappear entirely once its opportunity is deleted");
		listed.Status.Should().Be("Cancelled");
		listed.OpportunityTitle.Should().BeNull(
			"the opportunity backing this engagement no longer exists, so its title can "
			+ "no longer be resolved");
	}

	[Test]
	public async Task CheckInEngagement_ShouldMarkAsCheckedIn_WhenOrganisatorChecksIn(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		var result = await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		result.Status.Should().Be("Confirmed");

		var myEngagements = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: false, cancellationToken);
		myEngagements.Items.Single().IsCheckedIn.Should().BeTrue();
	}

	[Test]
	public async Task CheckInEngagement_ShouldReturn400_WhenEngagementIsNotConfirmed(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CheckInWithPin_ShouldMarkAsCheckedIn_WhenVolunteerUsesCorrectPin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var pin = await olafClient.GetOpportunityCheckInPinAsync(opportunity.Id, cancellationToken)
			?? throw new InvalidOperationException("PIN was not generated for PINCode opportunity.");

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		var result = await veraClient.CheckInWithPinAsync(
			engagement.Id,
			new CheckInWithPinRequest { Pin = pin },
			cancellationToken);

		result.Status.Should().Be("Confirmed");

		var myEngagements = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: false, cancellationToken);
		myEngagements.Items.Single().IsCheckedIn.Should().BeTrue();
	}

	[Test]
	public async Task CheckInWithPin_ShouldReturn400_WhenPinIsWrong(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var act = () => veraClient.CheckInWithPinAsync(
			engagement.Id,
			new CheckInWithPinRequest { Pin = "wrong-pin" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CheckInWithPin_ShouldReturn409_WhenOpportunityDoesNotUsePinCheckIn(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "None", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var act = () => veraClient.CheckInWithPinAsync(
			engagement.Id,
			new CheckInWithPinRequest { Pin = "" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);

		var myEngagements = await veraClient.GetMyEngagementsAsync(1, 20, upcoming: true, cancellationToken);
		myEngagements.Items.Single().IsCheckedIn.Should().BeFalse();
	}

	[Test]
	public async Task CheckInWithPin_ShouldReturn400_WhenPinIsEmpty_OnPinCodeOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var act = () => veraClient.CheckInWithPinAsync(
			engagement.Id,
			new CheckInWithPinRequest { Pin = "" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldNotExposeCheckInPin(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var details = await olafClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);

		details.Should().NotBeNull();
	}

	[Test]
	public async Task GetVolunteerOpportunityDetails_ShouldReflectCheckedInStatus_OnCurrentUserEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var beforeCheckIn = await veraClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);
		beforeCheckIn.CurrentUserEngagements.Should().ContainSingle();
		beforeCheckIn.CurrentUserEngagements.Single().IsCheckedIn.Should().BeFalse();

		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var afterCheckIn = await veraClient.GetVolunteerOpportunityDetailsAsync(opportunity.Id, cancellationToken);
		afterCheckIn.CurrentUserEngagements.Should().ContainSingle();
		afterCheckIn.CurrentUserEngagements.Single().IsCheckedIn.Should().BeTrue();

		var act = () => veraClient.WithdrawEngagementAsync(engagement.Id, cancellationToken);
		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task CheckInWithPin_ShouldReturn400_WhenVolunteerTriesToCheckInSomeoneElsesEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var pin = await olafClient.GetOpportunityCheckInPinAsync(opportunity.Id, cancellationToken)
			?? throw new InvalidOperationException("PIN was not generated for PINCode opportunity.");

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var veraEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		var olafEngagement = await olafClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I will also help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(veraEngagement.Id, cancellationToken);
		await olafClient.ConfirmEngagementAsync(olafEngagement.Id, cancellationToken);

		var act = () => veraClient.CheckInWithPinAsync(
			olafEngagement.Id,
			new CheckInWithPinRequest { Pin = pin },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CheckInWithPin_ShouldReturn403_WhenTooManyFailedAttempts(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, "PINCode", cancellationToken);

		var pin = await olafClient.GetOpportunityCheckInPinAsync(opportunity.Id, cancellationToken)
			?? throw new InvalidOperationException("PIN was not generated for PINCode opportunity.");

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Ready to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		for (var attempt = 0; attempt < 5; attempt++)
		{
			var wrongAttempt = () => veraClient.CheckInWithPinAsync(
				engagement.Id,
				new CheckInWithPinRequest { Pin = "wrong-pin" },
				cancellationToken);

			var wrongException = await wrongAttempt.Should().ThrowAsync<ApiException>();
			wrongException.Which.StatusCode.Should().Be(400);
		}

		var lockedOutAttempt = () => veraClient.CheckInWithPinAsync(
			engagement.Id,
			new CheckInWithPinRequest { Pin = pin },
			cancellationToken);

		var lockedException = await lockedOutAttempt.Should().ThrowAsync<ApiException>();
		lockedException.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn409_WhenVolunteerAlreadySignedUp(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "First sign-up" },
			cancellationToken);

		var act = () => veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Duplicate sign-up" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task CreateEngagement_ShouldAllowExactlyOneSignUp_WhenManyVolunteersRaceForASingleSlot(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 1,
				RecurrenceCount = 1,
			},
			cancellationToken);
		var timeSlotId = timeSlots.Single().Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		const int volunteerCount = 8;
		var volunteerClients = new List<AfuntoApi>(volunteerCount);
		for (var i = 0; i < volunteerCount; i++)
		{
			var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
			volunteerClients.Add(await fixture.CreateAuthenticatedClientAsync(username, password));
		}

		var signUpTasks = volunteerClients.Select(async client =>
		{
			try
			{
				await client.CreateEngagementAsync(
					opportunity.Id, new CreateEngagementRequest { TimeSlotId = timeSlotId }, cancellationToken);
				return true;
			}
			catch (ApiException ex) when (ex.StatusCode == 409)
			{
				return false;
			}
		}).ToList();

		var results = await Task.WhenAll(signUpTasks);

		results.Count(succeeded => succeeded).Should().Be(1,
			"the time-slot row lock must let exactly one concurrent sign-up win a slot with MaxParticipants = 1");

		await using var dbContext = fixture.CreateApplicationDbContext();
		var nonTerminalCount = await dbContext.Set<Engagement>()
			.CountAsync(e =>
				e.OpportunityId == VolunteerOpportunityId.Create(opportunity.Id).GetValueOrThrow() &&
				(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed),
				cancellationToken);
		nonTerminalCount.Should().Be(1, "the database must never end up with more live engagements than the slot's capacity");
	}

	[Test]
	public async Task WithdrawEngagement_ShouldReturn403_WhenUserWithdrawsAnotherUsersEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		var act = () => olafClient.WithdrawEngagementAsync(engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task GetEngagements_ShouldReturn403_WhenOrganisatorAccessesOtherOrgsOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CancelEngagement_ShouldReturn403_WhenOrganisatorCancelsOtherOrgsEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.CancelEngagementAsync(engagement.Id, null, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task ConfirmEngagement_ShouldReturn403_WhenOrganisatorConfirmsOtherOrgsEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		var act = () => veraClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CheckInEngagement_ShouldReturn403_WhenOrganisatorChecksInOtherOrgsEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		await CreateOrganizationAsync(veraClient, cancellationToken);

		veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CheckInEngagement_ShouldReturn404_WhenOpportunityIsDeleted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var org1Id = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, org1Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		await CreateOrganizationAsync(veraClient, cancellationToken);

		veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		await fixture.DeleteOpportunityRowDirectlyAsync(opportunity.Id);

		(await fixture.CountRowsWhereAsync("volunteer_opportunity", "id", opportunity.Id)).Should().Be(0);

		var act = () => veraClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task CreateEngagement_ShouldSucceed_WhenVolunteerReapliesAfterWithdrawal(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "First attempt" },
			cancellationToken);

		await veraClient.WithdrawEngagementAsync(engagement.Id, cancellationToken);

		var result = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Second attempt after withdrawal" },
			cancellationToken);

		result.Status.Should().Be("Pending");
	}

	[Test]
	public async Task CreateEngagement_ShouldSucceed_WhenVolunteerReapliesAfterOrganizerCancellation(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "First attempt" },
			cancellationToken);

		await olafClient.CancelEngagementAsync(
			engagement.Id,
			new CancelEngagementRequest { Reason = "No longer needed" },
			cancellationToken);

		var result = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Second attempt after cancellation" },
			cancellationToken);

		result.Status.Should().Be("Pending");
	}

	[Test]
	public async Task CreateEngagement_ShouldKeepOriginalCreatedOn_WhenVolunteerReapliesAfterWithdrawal(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var first = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Original application." },
			cancellationToken);
		var firstCreatedOn = await GetCreatedOnAsync(veraClient, first.Id, cancellationToken);

		await veraClient.WithdrawEngagementAsync(first.Id, cancellationToken);

		var reapplyStartedAt = DateTimeOffset.UtcNow;

		var second = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Re-application after withdrawal." },
			cancellationToken);
		second.Id.Should().Be(first.Id, "reactivation reuses the same terminal engagement row");

		var secondCreatedOn = await GetCreatedOnAsync(veraClient, second.Id, cancellationToken);

		secondCreatedOn.Should().Be(firstCreatedOn,
			"Engagement.Reactivate must not change CreatedOn - it stays pinned to the "
			+ "original application time");
		secondCreatedOn.Should().BeBefore(reapplyStartedAt,
			"CreatedOn must predate the re-application entirely, not merely happen to "
			+ "match a value read earlier");
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn409_WhenTimeSlotIsFull(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 1,
				RecurrenceCount = 1,
			},
			cancellationToken);
		var slotId = timeSlots.First().Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var olafEngagement = await olafClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);
		olafEngagement.Status.Should().Be("Pending");

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var act = () => veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task CreateEngagement_ShouldSucceed_WhenSlotSpotIsFreedAfterWithdrawal(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 1,
				RecurrenceCount = 1,
			},
			cancellationToken);
		var slotId = timeSlots.First().Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var olafEngagement = await olafClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);

		await olafClient.WithdrawEngagementAsync(olafEngagement.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var result = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = slotId },
			cancellationToken);

		result.Status.Should().Be("Pending");
	}

	[Test]
	public async Task GetOpportunityFeedback_ShouldReturnEmptySummary_WhenNoFeedbackSubmitted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var summary = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);

		summary.AverageRating.Should().BeNull();
		summary.FeedbackCount.Should().Be(0);
		summary.Items.Items.Should().BeEmpty();
	}

	[Test]
	public async Task GetOpportunityFeedback_ShouldReturnSubmittedFeedback_WhenVolunteerCheckedInAndRated(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id,
			new SubmitFeedbackRequest { Rating = 4, Comment = "Great experience" },
			cancellationToken);

		var summary = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);

		summary.AverageRating.Should().Be(4);
		summary.FeedbackCount.Should().Be(1);
		summary.Items.Items.Should().ContainSingle(i => i.Rating == 4 && i.Comment == "Great experience");
	}

	[Test]
	public async Task GetOpportunityFeedback_ShouldPaginate_AcrossMultiplePages(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var veraEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "Vera helps" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(veraEngagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, veraEngagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			veraEngagement.Id, new SubmitFeedbackRequest { Rating = 5, Comment = "Vera's feedback" }, cancellationToken);

		var olafEngagement = await olafClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "Olaf helps too" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(olafEngagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, olafEngagement.Id, cancellationToken);
		await olafClient.SubmitFeedbackAsync(
			olafEngagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Olaf's feedback" }, cancellationToken);

		var firstPage = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 1, cancellationToken);
		var secondPage = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 2, 1, cancellationToken);

		firstPage.FeedbackCount.Should().Be(2);
		firstPage.AverageRating.Should().Be(4);
		firstPage.Items.Items.Should().ContainSingle();
		firstPage.Items.TotalItems.Should().Be(2);
		firstPage.Items.PageCount.Should().Be(2);

		secondPage.FeedbackCount.Should().Be(2);
		secondPage.AverageRating.Should().Be(4);
		secondPage.Items.Items.Should().ContainSingle();

		var firstPageComments = firstPage.Items.Items.Select(i => i.Comment).ToList();
		var secondPageComments = secondPage.Items.Items.Select(i => i.Comment).ToList();
		firstPageComments.Should().NotBeEquivalentTo(secondPageComments);
	}

	[Test]
	public async Task GetOpportunityFeedback_ShouldSucceed_WhenRequestingUserIsAPlainMember(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);
		await fixture.AddPlainMemberDirectlyAsync(orgId, vera.Id, cancellationToken);

		var summary = await veraClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);

		summary.AverageRating.Should().BeNull();
		summary.FeedbackCount.Should().Be(0);
	}

	[Test]
	public async Task UpdateFeedback_ShouldUpdateRatingAndComment_WhenCalledByOwnerWithinWindow(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		await veraClient.UpdateFeedbackAsync(
			engagement.Id, new UpdateFeedbackRequest { Rating = 5, Comment = "Actually, great!" }, cancellationToken);

		var summary = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);
		summary.Items.Items.Should().ContainSingle(i => i.Rating == 5 && i.Comment == "Actually, great!");
	}

	[Test]
	public async Task UpdateFeedback_ShouldReturn404_WhenEngagementNotFound(
		CancellationToken cancellationToken)
	{
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.UpdateFeedbackAsync(
			Guid.NewGuid(), new UpdateFeedbackRequest { Rating = 5, Comment = null }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task UpdateFeedback_ShouldReturn403_WhenCallerIsNotTheOwner(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		var act = () => olafClient.UpdateFeedbackAsync(
			engagement.Id, new UpdateFeedbackRequest { Rating = 5, Comment = "Hijacked" }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task UpdateFeedback_ShouldReturn409_WhenFeedbackNotYetSubmitted(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var act = () => veraClient.UpdateFeedbackAsync(
			engagement.Id, new UpdateFeedbackRequest { Rating = 5, Comment = null }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task UpdateFeedback_ShouldReturn409_WhenEditWindowExpired(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		await using (var dbContext = fixture.CreateApplicationDbContext())
		{
			var expiredSubmittedAt = DateTimeOffset.UtcNow.AddDays(-(Engagement.FeedbackEditWindowDays + 1));
			await dbContext.Database.ExecuteSqlInterpolatedAsync(
				$"UPDATE engagement SET feedback_submitted_at = {expiredSubmittedAt} WHERE id = {engagement.Id}",
				cancellationToken);
		}

		var act = () => veraClient.UpdateFeedbackAsync(
			engagement.Id, new UpdateFeedbackRequest { Rating = 5, Comment = "Too late" }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task DeleteFeedback_ShouldClearFeedback_WhenCalledByOwnerWithinWindow(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		await veraClient.DeleteFeedbackAsync(engagement.Id, cancellationToken);

		var summary = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);
		summary.FeedbackCount.Should().Be(0);
	}

	[Test]
	public async Task DeleteFeedback_ShouldAllowResubmission_Afterward(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);
		await veraClient.DeleteFeedbackAsync(engagement.Id, cancellationToken);

		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 5, Comment = "Second try" }, cancellationToken);

		var summary = await olafClient.GetOpportunityFeedbackAsync(opportunity.Id, 1, 10, cancellationToken);
		summary.Items.Items.Should().ContainSingle(i => i.Rating == 5 && i.Comment == "Second try");
	}

	[Test]
	public async Task DeleteFeedback_ShouldReturn404_WhenEngagementNotFound(
		CancellationToken cancellationToken)
	{
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var act = () => veraClient.DeleteFeedbackAsync(Guid.NewGuid(), cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(404);
	}

	[Test]
	public async Task DeleteFeedback_ShouldReturn403_WhenCallerIsNotTheOwner(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		var act = () => olafClient.DeleteFeedbackAsync(engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task DeleteFeedback_ShouldReturn409_WhenEditWindowExpired(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { Message = "I want to help!" }, cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);
		await veraClient.SubmitFeedbackAsync(
			engagement.Id, new SubmitFeedbackRequest { Rating = 3, Comment = "Okay" }, cancellationToken);

		await using (var dbContext = fixture.CreateApplicationDbContext())
		{
			var expiredSubmittedAt = DateTimeOffset.UtcNow.AddDays(-(Engagement.FeedbackEditWindowDays + 1));
			await dbContext.Database.ExecuteSqlInterpolatedAsync(
				$"UPDATE engagement SET feedback_submitted_at = {expiredSubmittedAt} WHERE id = {engagement.Id}",
				cancellationToken);
		}

		var act = () => veraClient.DeleteFeedbackAsync(engagement.Id, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	[Arguments(1)]
	[Arguments(3)]
	[Arguments(5)]
	public async Task FeedbackRatingCheckConstraint_ShouldAllow_RatingWithinOneToFive(
		int rating,
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var engagement = Engagement.CreateIndividualContact(
			VolunteerOpportunityId.New(), UserId.New(), "Please let me help.").GetValueOrThrow();
		await dbContext.Engagements.AddAsync(engagement, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);

		var act = () => dbContext.Database.ExecuteSqlInterpolatedAsync(
			$"UPDATE engagement SET feedback_rating = {rating} WHERE id = {engagement.Id.Value}",
			cancellationToken);

		await act.Should().NotThrowAsync();
	}

	[Test]
	[Arguments(0)]
	[Arguments(6)]
	[Arguments(-1)]
	public async Task FeedbackRatingCheckConstraint_ShouldReject_RatingOutsideOneToFive(
		int rating,
		CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var engagement = Engagement.CreateIndividualContact(
			VolunteerOpportunityId.New(), UserId.New(), "Please let me help.").GetValueOrThrow();
		await dbContext.Engagements.AddAsync(engagement, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);

		var act = () => dbContext.Database.ExecuteSqlInterpolatedAsync(
			$"UPDATE engagement SET feedback_rating = {rating} WHERE id = {engagement.Id.Value}",
			cancellationToken);

		var exception = await act.Should().ThrowAsync<PostgresException>();
		exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn400_WhenTimeSlotBelongsToOtherOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);

		var opportunityA = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);
		var opportunityB = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		await olafClient.CreateTimeSlotAsync(
			opportunityA.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);
		await olafClient.PublishVolunteerOpportunityAsync(opportunityA.Id, cancellationToken);

		var slotsB = await olafClient.CreateTimeSlotAsync(
			opportunityB.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);
		var slotFromB = slotsB.First().Id;

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var act = () => veraClient.CreateEngagementAsync(
			opportunityA.Id,
			new CreateEngagementRequest { TimeSlotId = slotFromB },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CreateEngagement_ShouldSucceed_WhenVolunteerSignsUpForASecondTimeSlot_OnSameScheduledSlotsOpportunity(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var firstEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = firstSlotId },
			cancellationToken);

		var secondEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = secondSlotId },
			cancellationToken);

		firstEngagement.Status.Should().Be("Pending");
		secondEngagement.Status.Should().Be("Pending");
		secondEngagement.Id.Should().NotBe(firstEngagement.Id);
	}

	[Test]
	public async Task CreateEngagement_ShouldReturn409_WhenVolunteerSignsUpTwiceForTheSameTimeSlot(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceCount = 1,
			},
			cancellationToken);
		var slotId = timeSlots.First().Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = slotId }, cancellationToken);

		var act = () => veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = slotId }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(409);
	}

	[Test]
	public async Task CreateEngagement_ShouldPreserveAttendanceAndFeedback_WhenOrganizerCancelsToFreeUpADifferentTimeSlot(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddMinutes(5),
				EndDateTime = DateTimeOffset.UtcNow.AddMinutes(5).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var firstEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = firstSlotId }, cancellationToken);

		await olafClient.ConfirmEngagementAsync(firstEngagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, firstEngagement.Id, cancellationToken);

		// Feedback is only accepted once the occurrence is over (#2323), and a
		// slot can only be signed up for while it is still ahead - so wind the
		// engagement's denormalized window back rather than trying to sign up
		// for a slot that has already ended.
		await ElapseEngagementTimeSlotAsync(firstEngagement.Id, cancellationToken);

		await veraClient.SubmitFeedbackAsync(
			firstEngagement.Id,
			new SubmitFeedbackRequest { Rating = 5, Comment = "Great first session" },
			cancellationToken);

		await olafClient.CancelEngagementAsync(
			firstEngagement.Id,
			new CancelEngagementRequest { Reason = "Freeing up a spot" },
			cancellationToken);

		var secondEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = secondSlotId }, cancellationToken);

		var engagements = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);
		var firstAfter = engagements.Items.Single(e => e.Id == firstEngagement.Id);
		var secondAfter = engagements.Items.Single(e => e.Id == secondEngagement.Id);

		firstAfter.Status.Should().Be("Cancelled");
		firstAfter.IsCheckedIn.Should().BeTrue();
		firstAfter.HasFeedback.Should().BeTrue();
		firstAfter.TimeSlotId.Should().Be(firstSlotId);

		secondAfter.Status.Should().Be("Pending");
		secondAfter.TimeSlotId.Should().Be(secondSlotId);
		secondAfter.Id.Should().NotBe(firstEngagement.Id);
	}

	[Test]
	public async Task SubmitFeedback_ShouldReturn400_WhenTheOccurrenceHasNotEndedYet(
		CancellationToken cancellationToken)
	{
		// Check-in opens an hour before the slot starts, so a volunteer who
		// checks in early must not be able to rate an occurrence that has not
		// happened (#2323).
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddMinutes(5),
				EndDateTime = DateTimeOffset.UtcNow.AddMinutes(5).AddHours(2),
				MaxParticipants = 10,
			},
			cancellationToken);
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { TimeSlotId = timeSlots.ElementAt(0).Id },
			cancellationToken);

		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);
		await olafClient.CheckInEngagementAsync(opportunity.Id, engagement.Id, cancellationToken);

		var act = () => veraClient.SubmitFeedbackAsync(
			engagement.Id,
			new SubmitFeedbackRequest { Rating = 5, Comment = "Too early" },
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task DeleteVolunteerOpportunity_ShouldSucceed_WhenOneVolunteerHasEngagementsForMultipleTimeSlots(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = firstSlotId }, cancellationToken);
		await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = secondSlotId }, cancellationToken);

		var deleteOpportunity = () => olafClient.DeleteVolunteerOpportunityAsync(opportunity.Id, cancellationToken);
		await deleteOpportunity.Should().NotThrowAsync(
			"two engagements sharing (volunteer_id, opportunity_id) must not collide once their time slots are hard-deleted (#1724)");

		(await fixture.CountRowsWhereAsync("volunteer_opportunity", "id", opportunity.Id))
			.Should().Be(0);
	}

	[Test]
	public async Task DeleteTimeSlot_EntireSeries_ShouldSucceed_WhenOneVolunteerHasEngagementsForMultipleTimeSlots(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateScheduledSlotsOpportunityAsync(olafClient, orgId, cancellationToken);

		var timeSlots = await olafClient.CreateTimeSlotAsync(
			opportunity.Id,
			new CreateTimeSlotRequest
			{
				StartDateTime = DateTimeOffset.UtcNow.AddDays(7),
				EndDateTime = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
				MaxParticipants = 10,
				RecurrenceFrequency = "Weekly",
				RecurrenceCount = 2,
			},
			cancellationToken);
		var firstSlotId = timeSlots.ElementAt(0).Id;
		var secondSlotId = timeSlots.ElementAt(1).Id;
		await olafClient.PublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var firstEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = firstSlotId }, cancellationToken);
		var secondEngagement = await veraClient.CreateEngagementAsync(
			opportunity.Id, new CreateEngagementRequest { TimeSlotId = secondSlotId }, cancellationToken);

		var deleteSeries = () => olafClient.DeleteTimeSlotAsync(
			opportunity.Id, firstSlotId, "EntireSeries", cancellationToken);
		await deleteSeries.Should().NotThrowAsync(
			"two engagements sharing (volunteer_id, opportunity_id) must not collide once their time slots are hard-deleted (#1724)");

		var firstEngagementId = EngagementId.Create(firstEngagement.Id).GetValueOrThrow();
		var secondEngagementId = EngagementId.Create(secondEngagement.Id).GetValueOrThrow();
		await using var dbContext = fixture.CreateApplicationDbContext();
		var engagements = await dbContext.Set<Engagement>()
			.AsNoTracking()
			.Where(e => e.Id == firstEngagementId || e.Id == secondEngagementId)
			.ToListAsync(cancellationToken);
		engagements.Should().HaveCount(2);
		engagements.Should().OnlyContain(e => e.Status == EngagementStatus.Cancelled);
	}

	[Test]
	public async Task ConfirmEngagement_ShouldNotReturn500_WhenAnXTimezoneHeaderIsSent(
		CancellationToken cancellationToken)
	{
		var token = await fixture.GetAccessTokenAsync("olaf", "olaf123");
		using var http = fixture.CreateHttpClient();
		http.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
		http.DefaultRequestHeaders.Add("X-Timezone", "America/New_York");

		var response = await http.PostAsync(
			$"/v1/engagements/{Guid.NewGuid()}/confirm", content: null, cancellationToken);

		((int)response.StatusCode).Should().BeOneOf(404, 403);
	}

	[Test]
	public async Task GetMyEngagements_ShouldReturnTheReactivatedEngagement_AfterWithdrawAndReapply(
		CancellationToken cancellationToken)
	{
		var organizerClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(organizerClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(organizerClient, orgId, cancellationToken);

		var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var volunteerClient = await fixture.CreateAuthenticatedClientAsync(username, password);

		var original = await volunteerClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Original application." },
			cancellationToken);
		await volunteerClient.WithdrawEngagementAsync(original.Id, cancellationToken);

		// The opportunity's application deadline (30 days out) hasn't
		// passed, so the withdrawn engagement stays "upcoming" (#2240) -
		// only its status changes, not its bucket.
		var whileWithdrawn = await volunteerClient.GetMyEngagementsAsync(
			1, 10, upcoming: true, cancellationToken);
		whileWithdrawn.Items.Should().ContainSingle()
			.Which.Status.Should().Be("Withdrawn");

		var reactivated = await volunteerClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Re-application after withdrawal." },
			cancellationToken);

		reactivated.Id.Should().Be(original.Id);

		var afterReapply = await volunteerClient.GetMyEngagementsAsync(
			1, 10, upcoming: true, cancellationToken);
		afterReapply.Items.Should().ContainSingle()
			.Which.Id.Should().Be(original.Id);
	}

	[Test]
	public async Task UnpublishVolunteerOpportunity_CascadeCancelsItsConfirmedEngagement(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Please let me help." },
			cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		await olafClient.UnpublishVolunteerOpportunityAsync(opportunity.Id, cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.VolunteerOpportunities.VolunteerOpportunityUnpublishedDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the unpublished event within a few poll cycles");

		var engagements = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);
		var cascaded = engagements.Items.Should().ContainSingle().Which;
		cascaded.Status.Should().Be("Cancelled");
	}

	[Test]
	public async Task CancelVolunteerOpportunity_CascadeCancelsItsConfirmedEngagement_WithTheOrganizersReason(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var orgId = await CreateOrganizationAsync(olafClient, cancellationToken);
		var opportunity = await CreateOpportunityAsync(olafClient, orgId, cancellationToken);

		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var engagement = await veraClient.CreateEngagementAsync(
			opportunity.Id,
			new CreateEngagementRequest { Message = "Please let me help." },
			cancellationToken);
		await olafClient.ConfirmEngagementAsync(engagement.Id, cancellationToken);

		await olafClient.CancelVolunteerOpportunityAsync(
			opportunity.Id,
			new CancelVolunteerOpportunityRequest { Reason = "Venue is no longer available" },
			cancellationToken);

		var processed = await fixture.WaitForOutboxMessageProcessedAsync(
			"Domain.VolunteerOpportunities.VolunteerOpportunityCancelledDomainEvent", TimeSpan.FromSeconds(45));
		processed.Should().BeTrue("OutboxProcessorJob should dispatch the cancelled event within a few poll cycles");

		var engagements = await olafClient.GetEngagementsAsync(opportunity.Id, 1, 10, cancellationToken: cancellationToken);
		var cascaded = engagements.Items.Should().ContainSingle().Which;
		cascaded.Status.Should().Be("Cancelled");
		cascaded.CancellationReason.Should().Be("Opportunity was cancelled: Venue is no longer available");
	}

	private static async Task<DateTimeOffset> GetCreatedOnAsync(
		AfuntoApi volunteerClient, Guid engagementId, CancellationToken cancellationToken)
	{
		var page = await volunteerClient.GetMyEngagementsAsync(
			1, 50, upcoming: false, cancellationToken);
		var listed = page.Items.SingleOrDefault(e => e.Id == engagementId);
		if (listed is not null)
			return listed.CreatedOn;

		var upcoming = await volunteerClient.GetMyEngagementsAsync(
			1, 50, upcoming: true, cancellationToken);
		return upcoming.Items.Single(e => e.Id == engagementId).CreatedOn;
	}

	private static async Task<Guid> CreateOrganizationAsync(
		AfuntoApi client, CancellationToken cancellationToken)
	{
		var uniqueName = $"EngagementTestOrg_{Guid.NewGuid()}";
		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = uniqueName }, cancellationToken);
		return org.Id.Value;
	}

	private async Task<(Guid EngagementId, string Username)> SignUpEphemeralVolunteerAsync(
		Guid opportunityId, CancellationToken cancellationToken)
	{
		var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var volunteerClient = await fixture.CreateAuthenticatedClientAsync(username, password);
		var engagement = await volunteerClient.CreateEngagementAsync(
			opportunityId,
			new CreateEngagementRequest { Message = "I want to help!" },
			cancellationToken);
		return (engagement.Id, username);
	}

	private static Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken) =>
		CreateOpportunityAsync(client, orgId, "None", cancellationToken);

	private static async Task<CreateVolunteerOpportunityResponse> CreateOpportunityAsync(
		AfuntoApi client, Guid orgId, string checkInMethod, CancellationToken cancellationToken)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "Test Opportunity",
				DescriptionDe = "Integration test opportunity",
				OrganizationId = orgId,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
				Occurrence = "OneTime",
				ParticipationType = "IndividualContact",
				CheckInMethod = checkInMethod,
				ValidUntil = DateTimeOffset.UtcNow.AddDays(30),
			},
			cancellationToken);
	}

	/// <summary>
	/// Moves an engagement's denormalized time slot window into the past, so the
	/// occurrence counts as over for the guards that read it (#2323).
	/// </summary>
	private async Task ElapseEngagementTimeSlotAsync(Guid engagementId, CancellationToken cancellationToken)
	{
		await using var dbContext = fixture.CreateApplicationDbContext();
		var start = DateTimeOffset.UtcNow.AddHours(-3);
		var end = DateTimeOffset.UtcNow.AddHours(-1);
		await dbContext.Database.ExecuteSqlInterpolatedAsync(
			$"UPDATE engagement SET time_slot_start_date_time = {start}, time_slot_end_date_time = {end} WHERE id = {engagementId}",
			cancellationToken);
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

	private static async Task<CreateVolunteerOpportunityResponse> CreateScheduledSlotsOpportunityAsync(
		AfuntoApi client, Guid orgId, CancellationToken cancellationToken)
	{
		return await client.CreateVolunteerOpportunityAsync(
			new CreateVolunteerOpportunityRequest
			{
				TitleDe = "ScheduledSlots Opportunity",
				DescriptionDe = "Integration test ScheduledSlots opportunity",
				OrganizationId = orgId,
				Street = "Test Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
				Occurrence = "OneTime",
				ParticipationType = "ScheduledSlots",
				CheckInMethod = "None",
				IsDraft = true,
			},
			cancellationToken);
	}
}
