using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.VolunteerOpportunities.DeleteTimeSlot.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.DeleteTimeSlot;

public class DeleteTimeSlotCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly DeleteTimeSlotCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();

	public DeleteTimeSlotCommandHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_dbContext
			.CountActiveEngagementsForTimeSlotAsync(Arg.Any<TimeSlotId>(), Arg.Any<CancellationToken>())
			.Returns(0);
		_dbContext
			.GetActiveEngagementsForTimeSlotsAsync(Arg.Any<IReadOnlyCollection<TimeSlotId>>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_sut = new DeleteTimeSlotCommandHandler(_dbContext, NullLogger<DeleteTimeSlotCommandHandler>.Instance);
	}

	private VolunteerOpportunity CreateOpportunityWithTimeSlot(out TimeSlot timeSlot)
	{
		var opportunity = VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, false, Address.Create("Hauptstrasse", "1", "12345", "Berlin").Value,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;
		timeSlot = opportunity.AddTimeSlot(
			DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(7).AddHours(2), 10, DateTimeOffset.UtcNow).Value;
		return opportunity;
	}

	[Test]
	public async Task Handle_ShouldRemoveTimeSlot_WhenNoActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithTimeSlot(out var timeSlot);
		var opportunityId = opportunity.Id.Value;
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new DeleteTimeSlotCommand(opportunityId, timeSlot.Id.Value, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.DeletedTimeSlotIds.Should().ContainSingle().Which.Should().Be(timeSlot.Id.Value);
		opportunity.TimeSlots.Should().BeEmpty();
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithTimeSlot(out var timeSlot);
		var opportunityId = opportunity.Id.Value;
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);

		var command = new DeleteTimeSlotCommand(opportunityId, timeSlot.Id.Value, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		opportunity.TimeSlots.Should().ContainSingle();
	}

	private VolunteerOpportunity CreateOpportunityWithSeries(
		out TimeSlot slot1, out TimeSlot slot2, out TimeSlot slot3)
	{
		var opportunity = VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, false, Address.Create("Hauptstrasse", "1", "12345", "Berlin").Value,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;

		var seriesId = Guid.CreateVersion7();
		var baseStart = DateTimeOffset.UtcNow.AddDays(7);
		var baseEnd = baseStart.AddHours(2);

		slot1 = opportunity.AddTimeSlot(baseStart, baseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		slot2 = opportunity.AddTimeSlot(baseStart.AddDays(7), baseEnd.AddDays(7), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		slot3 = opportunity.AddTimeSlot(baseStart.AddDays(14), baseEnd.AddDays(14), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		return opportunity;
	}

	[Test]
	public async Task Handle_ShouldDeleteTargetAndFollowingSlots_WhenScopeIsThisAndFollowing(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithSeries(out var slot1, out var slot2, out var slot3);
		var opportunityId = opportunity.Id.Value;
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new DeleteTimeSlotCommand(opportunityId, slot2.Id.Value, DefaultRequestingUserId, SeriesEditScope.ThisAndFollowing);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.DeletedTimeSlotIds.Should().BeEquivalentTo([slot2.Id.Value, slot3.Id.Value]);
		opportunity.TimeSlots.Should().ContainSingle().Which.Id.Should().Be(slot1.Id);
	}

	[Test]
	public async Task Handle_ShouldDeleteEveryOccurrence_WhenScopeIsEntireSeries(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithSeries(out var slot1, out var slot2, out var slot3);
		var opportunityId = opportunity.Id.Value;
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new DeleteTimeSlotCommand(opportunityId, slot3.Id.Value, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.DeletedTimeSlotIds.Should().BeEquivalentTo([slot1.Id.Value, slot2.Id.Value, slot3.Id.Value]);
		opportunity.TimeSlots.Should().BeEmpty();
	}

	[Test]
	public async Task Handle_ShouldForceCancelActiveEngagements_AndNotifyVolunteers_ForBulkDelete(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithSeries(out var slot1, out var slot2, out _);
		var opportunityId = VolunteerOpportunityId.Create(opportunity.Id.Value).GetValueOrThrow();
		_opportunityRepo
			.FindAsync(opportunityId, cancellationToken)
			.Returns(opportunity);

		var pendingEngagement = Engagement.CreateSlotSignUp(opportunityId, UserId.New(), slot1.Id);
		var confirmedEngagement = Engagement.CreateSlotSignUp(opportunityId, UserId.New(), slot2.Id);
		confirmedEngagement.Confirm();

		_dbContext
			.GetActiveEngagementsForTimeSlotsAsync(Arg.Any<IReadOnlyCollection<TimeSlotId>>(), cancellationToken)
			.Returns([pendingEngagement, confirmedEngagement]);

		var command = new DeleteTimeSlotCommand(opportunity.Id.Value, slot1.Id.Value, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		pendingEngagement.Status.Should().Be(EngagementStatus.Cancelled);
		pendingEngagement.CancellationReason.Should().BeNull();
		confirmedEngagement.Status.Should().Be(EngagementStatus.Cancelled);

		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.Kind == NotificationKind.EngagementCancelled && n.RelatedEntityId == pendingEngagement.Id.Value),
			cancellationToken);
		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.Kind == NotificationKind.EngagementCancelled && n.RelatedEntityId == confirmedEngagement.Id.Value),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldExcludePastOccurrences_FromBulkDelete(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunity = VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, false, Address.Create("Hauptstrasse", "1", "12345", "Berlin").Value,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;
		var seriesId = Guid.CreateVersion7();
		var pastStart = DateTimeOffset.UtcNow.AddDays(-9);
		var slot1 = opportunity.AddTimeSlot(pastStart, pastStart.AddHours(2), 10, pastStart.AddDays(-1), seriesId, "Weekly", 2).Value;
		var slot2 = opportunity.AddTimeSlot(
			DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(7).AddHours(2), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new DeleteTimeSlotCommand(opportunityId, slot2.Id.Value, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.DeletedTimeSlotIds.Should().BeEquivalentTo([slot2.Id.Value]);
		opportunity.TimeSlots.Should().ContainSingle().Which.Id.Should().Be(slot1.Id);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenBulkScopeAndTimeSlotNotPartOfSeries(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateOpportunityWithTimeSlot(out var timeSlot);
		var opportunityId = opportunity.Id.Value;
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new DeleteTimeSlotCommand(opportunityId, timeSlot.Id.Value, DefaultRequestingUserId, SeriesEditScope.ThisAndFollowing);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not part of a recurring series*");
		opportunity.TimeSlots.Should().ContainSingle();
	}
}
