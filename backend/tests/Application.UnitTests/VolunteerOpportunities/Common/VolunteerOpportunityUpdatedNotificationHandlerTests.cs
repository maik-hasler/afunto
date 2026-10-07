using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Engagements;
using Application.VolunteerOpportunities.Common;
using AwesomeAssertions;
using Domain.Common;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.Common;

public class VolunteerOpportunityUpdatedNotificationHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IEngagementReadRepository _engagementReadRepository = Substitute.For<IEngagementReadRepository>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IEmailService _emailService = Substitute.For<IEmailService>();
	private readonly IEmailTemplateRenderer _emailTemplateRenderer = Substitute.For<IEmailTemplateRenderer>();
	private readonly IEmailLinkBuilder _emailLinkBuilder = Substitute.For<IEmailLinkBuilder>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly VolunteerOpportunityUpdatedNotificationHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;

	public VolunteerOpportunityUpdatedNotificationHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(CreateDefaultOpportunity());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_keycloakUserService
			.GetUserProfilesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => callInfo.Arg<IReadOnlyList<Guid>>()!
				.ToDictionary(id => id, id => new KeycloakUserProfile(id, "user", null, null, "user@example.com")));
		_emailService
			.SendBatchAsync(Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => callInfo.Arg<IReadOnlyList<EmailMessage>>()!.Select(_ => true).ToList());
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns(call => ((IReadOnlyCollection<UserId>)call[0]!).Select(User.Create).ToList());
		_emailTemplateRenderer
			.Render(Arg.Any<EmailDraft>())
			.Returns(callInfo => new RenderedEmail(
				"Test Subject", $"Test Body {callInfo.Arg<EmailDraft>().Placeholders["OpportunityTitle"]}", "<p>Html</p>", null));
		_sut = new VolunteerOpportunityUpdatedNotificationHandler(
			_dbContext, _engagementReadRepository, _keycloakUserService, _emailService, _emailTemplateRenderer, _emailLinkBuilder,
			NullLogger<VolunteerOpportunityUpdatedNotificationHandler>.Instance);
	}

	private VolunteerOpportunity CreateDefaultOpportunity(string title = "Geänderte Aktion") =>
		VolunteerOpportunity.Create(
			DefaultOrgId, title, null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime,
			ParticipationType.IndividualContact, CheckInMethod.None, _pinGenerator,
			validUntil: DateTimeOffset.UtcNow.AddDays(30)).Value;

	[Test]
	public async Task Handle_ShouldEmailActiveVolunteers(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var activeVolunteer = Guid.NewGuid();
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunityId, Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([activeVolunteer]);
		var notification = new VolunteerOpportunityUpdatedDomainEvent(opportunityId, null);

		// Act
		await _sut.Handle(notification, cancellationToken);

		// Assert
		await _emailService.Received(1).SendBatchAsync(
			Arg.Is<IReadOnlyList<EmailMessage>>(messages =>
				messages!.Count == 1 && messages[0].To == "user@example.com" && messages[0].Content.TextBody.Contains("Geänderte Aktion")),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenAnyNotificationEmailFailsToSend(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var activeVolunteer = Guid.NewGuid();
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunityId, Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([activeVolunteer]);
		_emailService.SendBatchAsync(Arg.Any<IReadOnlyList<EmailMessage>>(), cancellationToken)
			.Returns([false]);
		var notification = new VolunteerOpportunityUpdatedDomainEvent(opportunityId, null);

		// Act
		Func<Task> act = async () => await _sut.Handle(notification, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>(
			"a swallowed delivery failure here would let the outbox believe the notification was delivered when it never left the process");
	}

	[Test]
	public async Task Handle_ShouldEmailOnlyThatSlotsVolunteers_WithTheNewTime_WhenASlotWasRescheduled(
		CancellationToken cancellationToken)
	{
		// Arrange
		var now = DateTimeOffset.UtcNow;
		var opportunity = VolunteerOpportunity.Create(
			DefaultOrgId, "Tafel-Ausgabe", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime,
			ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;
		var slot = opportunity.AddTimeSlot(now.AddDays(3), now.AddDays(3).AddHours(2), 10, now).Value;
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunity.Id, slot.Id, cancellationToken)
			.Returns([Guid.NewGuid()]);

		// Act
		await _sut.Handle(new VolunteerOpportunityUpdatedDomainEvent(opportunity.Id, slot.Id), cancellationToken);

		// Assert
		_emailTemplateRenderer.Received(1).Render(Arg.Is<EmailDraft>(d =>
			d.Kind == EmailTemplateKind.TimeSlotRescheduled
			&& d.Facts.OfType<EmailFact.Schedule>().Single().Slots.Single().Start == slot.StartDateTime
			&& d.UnsubscribeUrl == null));
	}

	[Test]
	public async Task Handle_ShouldNameTheNewPlace_WhenTheOpportunityWasRelocated(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunityId, null, cancellationToken)
			.Returns([Guid.NewGuid()]);

		// Act
		await _sut.Handle(new VolunteerOpportunityUpdatedDomainEvent(opportunityId, null), cancellationToken);

		// Assert
		_emailTemplateRenderer.Received(1).Render(Arg.Is<EmailDraft>(d =>
			d.Kind == EmailTemplateKind.OpportunityLocationChanged
			&& d.Facts.OfType<EmailFact.Location>().Single().Address == "Hauptstraße 1, 12345 Berlin"));
	}

	[Test]
	public async Task Handle_ShouldSkip_WhenTheRescheduledSlotNoLongerExists(
		CancellationToken cancellationToken)
	{
		// Act
		await _sut.Handle(
			new VolunteerOpportunityUpdatedDomainEvent(VolunteerOpportunityId.New(), TimeSlotId.New()),
			cancellationToken);

		// Assert
		await _engagementReadRepository.DidNotReceive().GetActiveVolunteerIdsByOpportunityAsync(
			Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>());
		await _emailService.DidNotReceive().SendBatchAsync(
			Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRenderEmail_InVolunteersPreferredLanguage(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var activeVolunteerId = UserId.New();
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunityId, Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([activeVolunteerId.Value]);

		var activeVolunteer = User.Create(activeVolunteerId);
		activeVolunteer.SetPreferredLanguage("en");
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns([activeVolunteer]);

		var notification = new VolunteerOpportunityUpdatedDomainEvent(opportunityId, null);

		// Act
		await _sut.Handle(notification, cancellationToken);

		// Assert
		_emailTemplateRenderer.Received(1).Render(
			Arg.Is<EmailDraft>(d => d.Kind == EmailTemplateKind.OpportunityLocationChanged && d.Language == "en"));
	}

	[Test]
	public async Task Handle_ShouldSkipVolunteer_WhenKeycloakProfileLookupFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var activeVolunteer = Guid.NewGuid();
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunityId, Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([activeVolunteer]);
		_keycloakUserService
			.GetUserProfilesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
			.Returns(new Dictionary<Guid, KeycloakUserProfile>());

		var notification = new VolunteerOpportunityUpdatedDomainEvent(opportunityId, null);

		// Act
		await _sut.Handle(notification, cancellationToken);

		// Assert
		await _emailService.DidNotReceive().SendBatchAsync(
			Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDoNothing_WhenNoActiveVolunteers(
		CancellationToken cancellationToken)
	{
		// Arrange
		var notification = new VolunteerOpportunityUpdatedDomainEvent(VolunteerOpportunityId.New(), null);

		// Act
		await _sut.Handle(notification, cancellationToken);

		// Assert
		await _emailService.DidNotReceive().SendBatchAsync(
			Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldSkip_WhenOpportunityNoLongerExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunity?)null);
		var notification = new VolunteerOpportunityUpdatedDomainEvent(VolunteerOpportunityId.New(), null);

		// Act
		await _sut.Handle(notification, cancellationToken);

		// Assert
		await _emailService.DidNotReceive().SendBatchAsync(
			Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>());
	}
}
