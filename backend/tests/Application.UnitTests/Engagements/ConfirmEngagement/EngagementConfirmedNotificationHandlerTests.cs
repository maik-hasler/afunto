using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Engagements.ConfirmEngagement.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Engagements.ConfirmEngagement;

public class EngagementConfirmedNotificationHandlerTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
	private static readonly Address DefaultAddress = Address.Create("Teststraße", "1", "12345", "Berlin").Value;

	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IEmailService _emailService = Substitute.For<IEmailService>();
	private readonly IEmailTemplateRenderer _emailTemplateRenderer = Substitute.For<IEmailTemplateRenderer>();
	private readonly IEmailLinkBuilder _emailLinkBuilder = Substitute.For<IEmailLinkBuilder>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly EngagementConfirmedNotificationHandler _sut;

	private readonly UserId _volunteerId = UserId.New();
	private readonly VolunteerOpportunity _opportunity;
	private EmailDraft? _renderedDraft;

	public EngagementConfirmedNotificationHandlerTests()
	{
		_opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(), "Tafel-Ausgabe", "Food bank", "Beschreibung", null, false, DefaultAddress,
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
		_emailLinkBuilder.MySignUps().Returns("https://afunto.example/my-signups");
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns(call => ((IReadOnlyCollection<UserId>)call[0]!).Select(User.Create).ToList());

		_sut = new EngagementConfirmedNotificationHandler(
			_dbContext, _keycloakUserService, _emailService, _emailTemplateRenderer, _emailLinkBuilder,
			new FixedTimeProvider(Now), NullLogger<EngagementConfirmedNotificationHandler>.Instance);
	}

	[Test]
	public async Task Handle_ShouldSendOneEmailListingEveryDate_WhenSeveralEngagementsWereConfirmedTogether(
		CancellationToken cancellationToken)
	{
		var claimed = new[] { ConfirmedSlotEngagement(Now.AddDays(7)), ConfirmedSlotEngagement(Now.AddDays(14)), ConfirmedSlotEngagement(Now.AddDays(21)) };
		ClaimReturns(claimed);

		await _sut.Handle(EventFor(claimed[0]), cancellationToken);

		await _emailService.Received(1).SendAsync(
			Arg.Is<EmailMessage>(m => m.To == "vera@example.com"), cancellationToken);
		_renderedDraft!.Kind.Should().Be(EmailTemplateKind.EngagementConfirmed);
		_renderedDraft.Count.Should().Be(3);
		_renderedDraft.Facts.OfType<EmailFact.Schedule>().Single().Slots.Should().HaveCount(3);
		_renderedDraft.Facts.OfType<EmailFact.Location>().Single().Address.Should().Be("Teststraße 1, 12345 Berlin");
	}

	[Test]
	public async Task Handle_ShouldSendNothing_WhenASiblingEventAlreadyClaimedEveryEngagement(
		CancellationToken cancellationToken)
	{
		var engagement = ConfirmedSlotEngagement(Now.AddDays(7));
		ClaimReturns([]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		_emailTemplateRenderer.DidNotReceive().Render(Arg.Any<EmailDraft>());
		await _emailService.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldUseTheInterestTemplateWithoutDates_ForAnIndividualContactEngagement(
		CancellationToken cancellationToken)
	{
		var engagement = Engagement.CreateIndividualContact(_opportunity.Id, _volunteerId, "Ich helfe gern").Value;
		engagement.Confirm();
		ClaimReturns([engagement]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		_renderedDraft!.Kind.Should().Be(EmailTemplateKind.InterestConfirmed);
		_renderedDraft.Facts.Should().BeEmpty();
	}

	[Test]
	public async Task Handle_ShouldRenderInTheVolunteersLanguage_WithTheMatchingTitle(
		CancellationToken cancellationToken)
	{
		var volunteer = User.Create(_volunteerId);
		volunteer.SetPreferredLanguage("en");
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns([volunteer]);
		var engagement = ConfirmedSlotEngagement(Now.AddDays(7));
		ClaimReturns([engagement]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		_renderedDraft!.Language.Should().Be("en");
		_renderedDraft.Placeholders["OpportunityTitle"].Should().Be("Food bank");
		_renderedDraft.RecipientName.Should().Be("Vera");
	}

	[Test]
	public async Task Handle_ShouldClaimButNotEmail_WhenTheVolunteerOptedOut(
		CancellationToken cancellationToken)
	{
		var optedOut = User.Create(_volunteerId);
		optedOut.UpdateNotificationPreferences(
			notifyOnNewSignUp: true,
			notifyOnWithdrawal: true,
			notifyOnEngagementConfirmed: false,
			notifyOnEngagementReminder: true);
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns([optedOut]);
		var engagement = ConfirmedSlotEngagement(Now.AddHours(10));
		ClaimReturns([engagement]);

		await _sut.Handle(EventFor(engagement), cancellationToken);

		await _emailService.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().ReleaseStatusNotificationsAsync(
			Arg.Any<IReadOnlyCollection<EngagementId>>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().MarkRemindersSentAsync(
			Arg.Any<IReadOnlyCollection<EngagementId>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldReleaseTheClaimAndRethrow_WhenSendingFails(
		CancellationToken cancellationToken)
	{
		var engagement = ConfirmedSlotEngagement(Now.AddDays(7));
		ClaimReturns([engagement]);
		_emailService
			.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("SMTP down"));

		var act = () => _sut.Handle(EventFor(engagement), cancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
		await _dbContext.Received(1).ReleaseStatusNotificationsAsync(
			Arg.Is<IReadOnlyCollection<EngagementId>>(ids => ids.Single() == engagement.Id), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldMarkTheReminderSent_OnlyForDatesInsideTheReminderWindow(
		CancellationToken cancellationToken)
	{
		var tomorrow = ConfirmedSlotEngagement(Now.AddHours(20));
		var nextWeek = ConfirmedSlotEngagement(Now.AddDays(7));
		ClaimReturns([tomorrow, nextWeek]);

		await _sut.Handle(EventFor(tomorrow), cancellationToken);

		await _dbContext.Received(1).MarkRemindersSentAsync(
			Arg.Is<IReadOnlyCollection<EngagementId>>(ids => ids.SequenceEqual(new[] { tomorrow.Id })),
			Now,
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNeitherClaimNorEmail_WhenOpportunityNoLongerExists(
		CancellationToken cancellationToken)
	{
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunity?)null);

		await _sut.Handle(
			new EngagementConfirmedDomainEvent(EngagementId.New(), _volunteerId, VolunteerOpportunityId.New()),
			cancellationToken);

		await _dbContext.DidNotReceive().ClaimStatusNotificationsAsync(
			Arg.Any<UserId>(), Arg.Any<VolunteerOpportunityId>(), Arg.Any<EngagementStatus>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
		await _emailService.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
	}

	private Engagement ConfirmedSlotEngagement(DateTimeOffset start)
	{
		var slot = _opportunity.AddTimeSlot(start, start.AddHours(2), 10, Now).Value;
		var engagement = Engagement.CreateSlotSignUp(_opportunity.Id, _volunteerId, slot.Id, slot.StartDateTime, slot.EndDateTime);
		engagement.Confirm();
		return engagement;
	}

	private void ClaimReturns(IReadOnlyCollection<Engagement> claimed) =>
		_dbContext
			.ClaimStatusNotificationsAsync(_volunteerId, _opportunity.Id, EngagementStatus.Confirmed, Now, Arg.Any<CancellationToken>())
			.Returns([.. claimed]);

	private static EngagementConfirmedDomainEvent EventFor(Engagement engagement) =>
		new(engagement.Id, engagement.VolunteerId!.Value, engagement.OpportunityId);

	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
