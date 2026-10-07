using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Engagements;
using Application.VolunteerOpportunities.UpdateTimeSlot.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.UpdateTimeSlot;

public class UpdateTimeSlotCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IEngagementReadRepository _engagementReadRepository = Substitute.For<IEngagementReadRepository>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly UpdateTimeSlotCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static readonly Address DefaultAddress = Address.Create("Hauptstrasse", "1", "12345", "Berlin").Value;
	private static readonly DateTimeOffset BaseStart = DateTimeOffset.UtcNow.AddDays(7);
	private static readonly DateTimeOffset BaseEnd = DateTimeOffset.UtcNow.AddDays(7).AddHours(2);

	public UpdateTimeSlotCommandHandlerTests()
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
			.CountActiveEngagementsForTimeSlotsAsync(Arg.Any<IReadOnlyCollection<TimeSlotId>>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_sut = new UpdateTimeSlotCommandHandler(_dbContext, _engagementReadRepository);
	}

	private VolunteerOpportunity CreateScheduledSlotsOpportunity() =>
		VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, false, DefaultAddress,
			Occurrence.Recurring, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldUpdateTimeSlot_WhenValid(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var newStart = BaseStart.AddDays(1);
		var newEnd = BaseEnd.AddDays(1);
		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, newStart, newEnd, 20, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(1);
		result.SkippedTimeSlotIds.Should().BeEmpty();
		timeSlot.StartDateTime.Should().Be(newStart);
		timeSlot.EndDateTime.Should().Be(newEnd);
		timeSlot.MaxParticipants.Should().Be(20);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns((VolunteerOpportunity?)null);

		var command = new UpdateTimeSlotCommand(
			opportunityId, Guid.CreateVersion7(), BaseStart, BaseEnd, 10, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage($"*{opportunityId}*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenTimeSlotNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, Guid.CreateVersion7(), BaseStart, BaseEnd, 10, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not found*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenCapacityReducedBelowActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_dbContext
			.CountActiveEngagementsForTimeSlotAsync(timeSlot.Id, cancellationToken)
			.Returns(5);

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, BaseStart, BaseEnd, 3, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*5*");
		timeSlot.MaxParticipants.Should().Be(10);
	}

	[Test]
	public async Task Handle_ShouldAllowChangingToUnlimitedCapacity_RegardlessOfActiveEngagementCount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_dbContext
			.CountActiveEngagementsForTimeSlotAsync(timeSlot.Id, cancellationToken)
			.Returns(10);

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, BaseStart, BaseEnd, MaxParticipants: null, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(1);
		timeSlot.MaxParticipants.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenReducingFromUnlimitedToFixedCapacityBelowActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, null, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_dbContext
			.CountActiveEngagementsForTimeSlotAsync(timeSlot.Id, cancellationToken)
			.Returns(5);

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, BaseStart, BaseEnd, MaxParticipants: 3, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*5*");
		timeSlot.MaxParticipants.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldNotifyOnlyVolunteersOnTheEditedSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var editedSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var otherSlot = opportunity.AddTimeSlot(BaseStart.AddDays(2), BaseEnd.AddDays(2), 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;
		var editedSlotVolunteer = Guid.NewGuid();
		var otherSlotVolunteer = Guid.NewGuid();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), editedSlot.Id, cancellationToken)
			.Returns([editedSlotVolunteer]);

		var command = new UpdateTimeSlotCommand(
			opportunityId, editedSlot.Id.Value, BaseStart.AddHours(1), BaseEnd.AddHours(1), 10, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.Kind == NotificationKind.OpportunityUpdated && n.RecipientId.Value == editedSlotVolunteer),
			cancellationToken);
		await _notifRepo.DidNotReceive().AddAsync(
			Arg.Is<Notification>(n => n!.RecipientId.Value == otherSlotVolunteer),
			cancellationToken);
		opportunity.Events.OfType<VolunteerOpportunityUpdatedDomainEvent>()
			.Should().ContainSingle()
			.Which.TimeSlotId.Should().Be(editedSlot.Id);
	}

	[Test]
	public async Task Handle_ShouldRaiseUpdatedDomainEvent_EvenWhenNoActiveVolunteersOnEditedSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var editedSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, editedSlot.Id.Value, BaseStart.AddHours(1), BaseEnd.AddHours(1), 10, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
		opportunity.Events.OfType<VolunteerOpportunityUpdatedDomainEvent>()
			.Should().ContainSingle()
			.Which.TimeSlotId.Should().Be(editedSlot.Id);
	}

	[Test]
	public async Task Handle_ShouldNotifyNobody_WhenOnlyTheCapacityOfASlotChanges(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var editedSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		_opportunityRepo
			.FindAsync(opportunity.Id, cancellationToken)
			.Returns(opportunity);
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunity.Id, editedSlot.Id, cancellationToken)
			.Returns([Guid.NewGuid()]);

		var command = new UpdateTimeSlotCommand(
			opportunity.Id.Value, editedSlot.Id.Value, BaseStart, BaseEnd, 20, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert - more room on the slot changes nothing for anyone already on it (#2402)
		editedSlot.MaxParticipants.Should().Be(20);
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
		opportunity.Events.Should().NotContain(e => e is VolunteerOpportunityUpdatedDomainEvent);
	}

	[Test]
	public async Task Handle_ShouldNotifyNobody_WhenTheCapacityOfASeriesChanges(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var slot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2).Value;
		opportunity.AddTimeSlot(BaseStart.AddDays(7), BaseEnd.AddDays(7), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2);
		_opportunityRepo
			.FindAsync(opportunity.Id, cancellationToken)
			.Returns(opportunity);
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([Guid.NewGuid()]);

		var command = new UpdateTimeSlotCommand(
			opportunity.Id.Value, slot.Id.Value, null, null, 20, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
		opportunity.Events.Should().NotContain(e => e is VolunteerOpportunityUpdatedDomainEvent);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, BaseStart.AddDays(1), BaseEnd.AddDays(1), 20, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		timeSlot.StartDateTime.Should().Be(BaseStart);
		timeSlot.EndDateTime.Should().Be(BaseEnd);
		timeSlot.MaxParticipants.Should().Be(10);
	}

	[Test]
	public async Task Handle_ShouldUpdateCapacityOnTargetAndFollowingSlots_WhenScopeIsThisAndFollowing(
		CancellationToken cancellationToken)
	{
		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var slot1 = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var slot2 = opportunity.AddTimeSlot(BaseStart.AddDays(7), BaseEnd.AddDays(7), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var slot3 = opportunity.AddTimeSlot(BaseStart.AddDays(14), BaseEnd.AddDays(14), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, slot2.Id.Value, null, null, 20, DefaultRequestingUserId, SeriesEditScope.ThisAndFollowing);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(2);
		result.SkippedTimeSlotIds.Should().BeEmpty();
		slot1.MaxParticipants.Should().Be(10);
		slot2.MaxParticipants.Should().Be(20);
		slot3.MaxParticipants.Should().Be(20);
	}

	[Test]
	public async Task Handle_ShouldUpdateCapacityOnEveryOccurrence_WhenScopeIsEntireSeries(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var slot1 = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var slot2 = opportunity.AddTimeSlot(BaseStart.AddDays(7), BaseEnd.AddDays(7), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var slot3 = opportunity.AddTimeSlot(BaseStart.AddDays(14), BaseEnd.AddDays(14), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 3).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, slot3.Id.Value, null, null, 25, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(3);
		slot1.MaxParticipants.Should().Be(25);
		slot2.MaxParticipants.Should().Be(25);
		slot3.MaxParticipants.Should().Be(25);
	}

	[Test]
	public async Task Handle_ShouldNotChangeDates_ForBulkScope(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var slot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 1).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, slot.Id.Value, null, null, 20, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		slot.StartDateTime.Should().Be(BaseStart);
		slot.EndDateTime.Should().Be(BaseEnd);
		slot.MaxParticipants.Should().Be(20);
	}

	[Test]
	public async Task Handle_ShouldSkipSlot_WhenCapacityBelowActiveEngagementsInBulkScope(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var slot1 = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2).Value;
		var slot2 = opportunity.AddTimeSlot(BaseStart.AddDays(7), BaseEnd.AddDays(7), 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.CountActiveEngagementsForTimeSlotsAsync(Arg.Any<IReadOnlyCollection<TimeSlotId>>(), Arg.Any<CancellationToken>())
			.Returns(new Dictionary<TimeSlotId, int> { [slot2.Id] = 8 });

		var command = new UpdateTimeSlotCommand(
			opportunityId, slot1.Id.Value, null, null, 3, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(1);
		result.SkippedTimeSlotIds.Should().ContainSingle().Which.Should().Be(slot2.Id.Value);
		slot1.MaxParticipants.Should().Be(3);
		slot2.MaxParticipants.Should().Be(10);
	}

	[Test]
	public async Task Handle_ShouldExcludePastOccurrences_FromBulkScope(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunity = CreateScheduledSlotsOpportunity();
		var seriesId = Guid.CreateVersion7();
		var pastStart = DateTimeOffset.UtcNow.AddDays(-9);
		var pastEnd = pastStart.AddHours(2);
		var slot1 = opportunity.AddTimeSlot(pastStart, pastEnd, 10, pastStart.AddDays(-1), seriesId, "Weekly", 2).Value;
		var slot2 = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow, seriesId, "Weekly", 2).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, slot2.Id.Value, null, null, 30, DefaultRequestingUserId, SeriesEditScope.EntireSeries);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.UpdatedCount.Should().Be(1);
		slot1.MaxParticipants.Should().Be(10);
		slot2.MaxParticipants.Should().Be(30);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenBulkScopeAndTimeSlotNotPartOfSeries(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreateScheduledSlotsOpportunity();
		var timeSlot = opportunity.AddTimeSlot(BaseStart, BaseEnd, 10, DateTimeOffset.UtcNow).Value;
		var opportunityId = opportunity.Id.Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UpdateTimeSlotCommand(
			opportunityId, timeSlot.Id.Value, null, null, 20, DefaultRequestingUserId, SeriesEditScope.ThisAndFollowing);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not part of a recurring series*");
		timeSlot.MaxParticipants.Should().Be(10);
	}
}
