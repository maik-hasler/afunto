using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Engagements.CancelEngagement.v1;
using AwesomeAssertions;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Engagements.CancelEngagement;

public class EngagementCancelledNotificationHandlerTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IEmailService _emailService = Substitute.For<IEmailService>();
	private readonly IEmailTemplateRenderer _emailTemplateRenderer = Substitute.For<IEmailTemplateRenderer>();
	private readonly IEmailLinkBuilder _emailLinkBuilder = Substitute.For<IEmailLinkBuilder>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly EngagementCancelledNotificationHandler _sut;

	private readonly UserId _volunteerId = UserId.New();
	private readonly VolunteerOpportunity _opportunity;
	private EmailDraft? _renderedDraft;

	public EngagementCancelledNotificationHandlerTests()
	{
		_opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(), "Tafel-Ausgabe", null, "Beschreibung", null, true, null,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;

		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(_opportunity);
		_keycloakUserService
			.GetUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new KeycloakUserProfile(Guid.NewGuid(), "vera", "Vera", null, "vera@example.com"));
		_emailTemplateRenderer
			.Render(Arg.Do<EmailDraft>(draft => _renderedDraft = draft))
			.Returns(new RenderedEmail("Subject", "Text", "<p>Html</p>", null));
		_emailLinkBuilder.Opportunities().Returns("https://afunto.example/opportunities");
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns(call => ((IReadOnlyCollection<UserId>)call[0]!).Select(User.Create).ToList());

		_sut = new EngagementCancelledNotificationHandler(
			_dbContext, _keycloakUserService, _emailService, _emailTemplateRenderer, _emailLinkBuilder,
			new FixedTimeProvider(Now), NullLogger<EngagementCancelledNotificationHandler>.Instance);
	}

	[Test]
	public async Task Handle_ShouldSendOneEmailForAWholeCancelledSeries(
		CancellationToken cancellationToken)
	{
		var claimed = new[] { CancelledSlotEngagement(Now.AddDays(7)), CancelledSlotEngagement(Now.AddDays(14)) };
		ClaimReturns(claimed);

		await _sut.Handle(EventFor(claimed[0]), cancellationToken);

		await _emailService.Received(1).SendAsync(
			Arg.Is<EmailMessage>(m => m.To == "vera@example.com"), cancellationToken);
		_renderedDraft!.Kind.Should().Be(EmailTemplateKind.EngagementCancelled);
		_renderedDraft.Count.Should().Be(2);
		_renderedDraft.Facts.OfType<EmailFact.Schedule>().Single().Slots.Should().HaveCount(2);
	}

	[Test]
	public async Task Handle_ShouldEmailEvenAVolunteerWhoOptedOutOfEverythingElse(
		CancellationToken cancellationToken)
	{
		var optedOut = User.Create(_volunteerId);
		optedOut.UpdateNotificationPreferences(
			notifyOnNewSignUp: false,
			notifyOnWithdrawal: false,
			notifyOnEngagementConfirmed: false,
			notifyOnEngagementReminder: false);
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns([optedOut]);
		var engagement = CancelledSlotEngagement(Now.AddDays(7));
		ClaimReturns([engagement]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		await _emailService.Received(1).SendAsync(Arg.Any<EmailMessage>(), cancellationToken);
		_renderedDraft!.UnsubscribeUrl.Should().BeNull("a cancellation cannot be unsubscribed from");
	}

	[Test]
	public async Task Handle_ShouldListEachDistinctReasonOnce(
		CancellationToken cancellationToken)
	{
		var first = CancelledSlotEngagement(Now.AddDays(7), "Zu wenig Helfende");
		var second = CancelledSlotEngagement(Now.AddDays(14), "Zu wenig Helfende");
		var withoutReason = CancelledSlotEngagement(Now.AddDays(21));
		ClaimReturns([first, second, withoutReason]);

		await _sut.Handle(EventFor(first), cancellationToken);

		_renderedDraft!.Facts.OfType<EmailFact.Reason>().Should().ContainSingle()
			.Which.Text.Should().Be("Zu wenig Helfende");
	}

	[Test]
	public async Task Handle_ShouldUseTheInterestTemplate_ForAnIndividualContactEngagement(
		CancellationToken cancellationToken)
	{
		var engagement = Engagement.CreateIndividualContact(_opportunity.Id, _volunteerId, "Ich helfe gern").Value;
		engagement.Cancel();
		ClaimReturns([engagement]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		_renderedDraft!.Kind.Should().Be(EmailTemplateKind.InterestCancelled);
	}

	[Test]
	public async Task Handle_ShouldFallBackToTheEventsTitleAndSnapshotDates_WhenTheOpportunityWasDeleted(
		CancellationToken cancellationToken)
	{
		var engagement = CancelledSlotEngagement(Now.AddDays(7));
		ClaimReturns([engagement]);
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunity?)null);

		await _sut.Handle(EventFor(engagement, opportunityTitle: "Gelöschter Einsatz"), cancellationToken);

		_renderedDraft!.Placeholders["OpportunityTitle"].Should().Be("Gelöschter Einsatz");
		_renderedDraft.Facts.OfType<EmailFact.Schedule>().Single().Slots.Single().Start
			.Should().Be(engagement.TimeSlotStartDateTime!.Value);
	}

	[Test]
	public async Task Handle_ShouldNeitherClaimNorEmail_WhenNoTitleIsAvailable(
		CancellationToken cancellationToken)
	{
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunity?)null);

		await _sut.Handle(
			new EngagementCancelledDomainEvent(EngagementId.New(), _volunteerId, VolunteerOpportunityId.New(), null),
			cancellationToken);

		await _dbContext.DidNotReceive().ClaimStatusNotificationsAsync(
			Arg.Any<UserId>(), Arg.Any<VolunteerOpportunityId>(), Arg.Any<EngagementStatus>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
		await _emailService.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldSendNothing_WhenASiblingEventAlreadyClaimedEveryEngagement(
		CancellationToken cancellationToken)
	{
		var engagement = CancelledSlotEngagement(Now.AddDays(7));
		ClaimReturns([]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		await _emailService.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldReleaseTheClaimAndRethrow_WhenSendingFails(
		CancellationToken cancellationToken)
	{
		var engagement = CancelledSlotEngagement(Now.AddDays(7));
		ClaimReturns([engagement]);
		_emailService
			.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("SMTP down"));

		var act = () => _sut.Handle(EventFor(engagement), cancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
		await _dbContext.Received(1).ReleaseStatusNotificationsAsync(
			Arg.Is<IReadOnlyCollection<EngagementId>>(ids => ids.Single() == engagement.Id), Arg.Any<CancellationToken>());
	}

	private Engagement CancelledSlotEngagement(DateTimeOffset start, string? reason = null)
	{
		var slot = _opportunity.AddTimeSlot(start, start.AddHours(2), 10, Now).Value;
		var engagement = Engagement.CreateSlotSignUp(_opportunity.Id, _volunteerId, slot.Id, slot.StartDateTime, slot.EndDateTime);
		engagement.Cancel(reason);
		return engagement;
	}

	private void ClaimReturns(IReadOnlyCollection<Engagement> claimed) =>
		_dbContext
			.ClaimStatusNotificationsAsync(_volunteerId, Arg.Any<VolunteerOpportunityId>(), EngagementStatus.Cancelled, Now, Arg.Any<CancellationToken>())
			.Returns([.. claimed]);

	private static EngagementCancelledDomainEvent EventFor(Engagement engagement, string? opportunityTitle = null) =>
		new(engagement.Id, engagement.VolunteerId!.Value, engagement.OpportunityId, engagement.CancellationReason, opportunityTitle);

	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
