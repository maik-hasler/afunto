using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Engagements.CreateEngagement.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.Engagements.CreateEngagement;

public class CreateEngagementCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IKeycloakOrganizationService _keycloakService =
		Substitute.For<IKeycloakOrganizationService>();
	private readonly IAggregateRepository<Engagement, EngagementId> _engagementRepo =
		Substitute.For<IAggregateRepository<Engagement, EngagementId>>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly CreateEngagementCommandHandler _sut;

	private static readonly Address TestAddress = Address.Create("Main St", "1", "12345", "Berlin").Value;

	private VolunteerOpportunity CreateTestOpportunity(
		VolunteerOpportunityId id, OpportunityStatus status = OpportunityStatus.Published)
	{
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(),
			"Test Opportunity",
			null,
			"Description",
			null,
			false,
			TestAddress,
			Occurrence.OneTime,
			ParticipationType.ScheduledSlots,
			CheckInMethod.None,
			_pinGenerator,
			status: OpportunityStatus.Draft).Value;

		if (status == OpportunityStatus.Draft)
			return opportunity;

		_ = opportunity.AddTimeSlot(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
			maxParticipants: 10,
			DateTimeOffset.UtcNow).Value;
		opportunity.Publish().ThrowIfFailure();

		if (status == OpportunityStatus.Unpublished)
			opportunity.Unpublish().ThrowIfFailure();
		else if (status == OpportunityStatus.Cancelled)
			opportunity.Cancel().ThrowIfFailure();

		return opportunity;
	}

	public CreateEngagementCommandHandlerTests()
	{
		_dbContext.Engagements.Returns(_engagementRepo);
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_keycloakService.GetMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_dbContext.CountActiveEngagementsForTimeSlotAsync(Arg.Any<TimeSlotId>(), Arg.Any<CancellationToken>())
			.Returns(0);
		_dbContext.GetTerminalEngagementAsync(Arg.Any<UserId>(), Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns((Engagement?)null);
		_sut = new CreateEngagementCommandHandler(_dbContext, _keycloakService);
	}

	private void SetupOpportunityExists(
		VolunteerOpportunityId opportunityId, OpportunityStatus status = OpportunityStatus.Published)
	{
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(),
			"Test Opportunity",
			null,
			"Description",
			null,
			false,
			TestAddress,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			_pinGenerator,
			status: OpportunityStatus.Draft,
			validUntil: DateTimeOffset.UtcNow.AddDays(30)).Value;

		if (status != OpportunityStatus.Draft)
		{
			opportunity.Publish().ThrowIfFailure();

			if (status == OpportunityStatus.Unpublished)
				opportunity.Unpublish().ThrowIfFailure();
			else if (status == OpportunityStatus.Cancelled)
				opportunity.Cancel().ThrowIfFailure();
		}

		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>())
			.Returns(opportunity);
	}

	private TimeSlotId SetupOpportunityExistsWithTimeSlot(
		VolunteerOpportunityId opportunityId, int? maxParticipants = 10, OpportunityStatus status = OpportunityStatus.Published)
	{
		var opportunity = CreateTestOpportunity(opportunityId, status);
		var timeSlot = opportunity.AddTimeSlot(
			DateTimeOffset.UtcNow.AddDays(1),
			DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
			maxParticipants,
			DateTimeOffset.UtcNow).Value;
		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>())
			.Returns(opportunity);
		return timeSlot.Id;
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityDoesNotExist(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunity?)null);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(),
			TimeSlotId.New(), Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not found*");
	}

	private void SetupMembersOnlyOpportunity(VolunteerOpportunityId opportunityId)
	{
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(),
			"Test Opportunity",
			null,
			"Description",
			null,
			false,
			TestAddress,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			_pinGenerator,
			status: OpportunityStatus.Draft,
			validUntil: DateTimeOffset.UtcNow.AddDays(30),
			membersOnly: true).Value;
		opportunity.Publish().ThrowIfFailure();

		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>())
			.Returns(opportunity);
	}

	[Test]
	public async Task Handle_ShouldThrowNotFound_WhenMembersOnlyAndVolunteerIsNotMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		SetupMembersOnlyOpportunity(opportunityId);
		_dbContext.IsMemberAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId: null, "Ich helfe gerne!");

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not found*");
	}

	[Test]
	public async Task Handle_ShouldCreateEngagement_WhenMembersOnlyAndVolunteerIsMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		SetupMembersOnlyOpportunity(opportunityId);
		_dbContext.IsMemberAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId: null, "Ich helfe gerne!");

		// Act
		var engagement = await _sut.Handle(command, cancellationToken);

		// Assert
		engagement.OpportunityId.Should().Be(opportunityId);
	}

	[Test]
	[Arguments(OpportunityStatus.Draft)]
	[Arguments(OpportunityStatus.Unpublished)]
	[Arguments(OpportunityStatus.Cancelled)]
	public async Task Handle_ShouldThrow_WhenOpportunityIsNotPublished(
		OpportunityStatus status, CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		SetupOpportunityExists(opportunityId, status);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId: null, "Ich helfe gerne!");

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenTimeSlotHasAlreadyEnded(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = VolunteerOpportunityId.New();
		var opportunity = CreateTestOpportunity(opportunityId, OpportunityStatus.Draft);
		var pastNow = DateTimeOffset.UtcNow.AddDays(-11);
		var timeSlot = opportunity.AddTimeSlot(
			DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-9), 10, pastNow).Value;
		opportunity.Publish().ThrowIfFailure();
		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>()).Returns(opportunity);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), timeSlot.Id, Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenScheduledSlotsOpportunityIsSignedUpWithoutATimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = VolunteerOpportunityId.New();
		SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId: null, "I'd like to help");

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenIndividualContactOpportunityIsSignedUpWithATimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		SetupOpportunityExists(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId.New(), Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenIndividualContactApplicationDeadlineHasPassed(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var farPast = DateTimeOffset.UtcNow.AddDays(-30);
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(),
			"Test Opportunity",
			null,
			"Description",
			null,
			false,
			TestAddress,
			Occurrence.OneTime,
			ParticipationType.IndividualContact,
			CheckInMethod.None,
			_pinGenerator,
			status: OpportunityStatus.Draft,
			validUntil: farPast.AddDays(1),
			now: farPast).Value;
		opportunity.Publish().ThrowIfFailure();
		_opportunityRepo.FindAsync(opportunityId, Arg.Any<CancellationToken>()).Returns(opportunity);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), TimeSlotId: null, "Ich helfe gerne!");

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldCreateScheduledSlotsEngagement_WhenTimeSlotIdIsProvided(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.TimeSlotId.Should().Be(timeSlotId);
		result.Message.Should().BeNull();
		result.Status.Should().Be(EngagementStatus.Pending);
	}

	[Test]
	public async Task Handle_ShouldCreateIndividualContactEngagement_WhenTimeSlotIdIsNull(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		SetupOpportunityExists(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, TimeSlotId: null, "Ich helfe gerne!");

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.TimeSlotId.Should().BeNull();
		result.Message.Should().Be("Ich helfe gerne!");
		result.Status.Should().Be(EngagementStatus.Pending);
	}

	[Test]
	public async Task Handle_ShouldPersistEngagement_ToRepository(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(
			opportunityId,
			UserId.New(),
			timeSlotId,
			Message: null);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _engagementRepo.Received(1).AddAsync(Arg.Any<Engagement>(), cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenTimeSlotIdIsNullAndMessageIsNull(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		SetupOpportunityExists(opportunityId);
		var command = new CreateEngagementCommand(
			opportunityId,
			UserId.New(),
			TimeSlotId: null,
			Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
	}

	[Test]
	public async Task Handle_ShouldSetCorrectOpportunityId_OnCreatedEngagement(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(
			opportunityId,
			UserId.New(),
			timeSlotId,
			Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.OpportunityId.Should().Be(opportunityId);
	}

	[Test]
	public async Task Handle_ShouldSetCorrectVolunteerId_OnCreatedEngagement(
		CancellationToken cancellationToken)
	{
		// Arrange
		var volunteerId = UserId.New();
		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(
			opportunityId,
			volunteerId,
			timeSlotId,
			Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.VolunteerId.Should().Be(volunteerId);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenTimeSlotIsFull(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId, maxParticipants: 3);
		_dbContext.CountActiveEngagementsForTimeSlotAsync(timeSlotId, Arg.Any<CancellationToken>())
			.Returns(3);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), timeSlotId, Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldLockTheTimeSlot_BeforeCountingActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), timeSlotId, Message: null);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).LockTimeSlotForUpdateAsync(timeSlotId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldSucceed_WhenTimeSlotHasUnlimitedCapacity_RegardlessOfActiveEngagementCount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId, maxParticipants: null);
		_dbContext.CountActiveEngagementsForTimeSlotAsync(timeSlotId, Arg.Any<CancellationToken>())
			.Returns(1000);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.TimeSlotId.Should().Be(timeSlotId);
		result.Status.Should().Be(EngagementStatus.Pending);
	}

	[Test]
	public async Task Handle_ShouldCheckForDuplicateSignUp_ScopedToTheRequestedTimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert

		await _dbContext.Received(1).HasEngagementAsync(volunteerId, opportunityId, timeSlotId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldLookUpTerminalEngagement_ScopedToTheRequestedTimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert

		await _dbContext.Received(1).GetTerminalEngagementAsync(volunteerId, opportunityId, timeSlotId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldReuseTerminalEngagement_WhenOneExistsForTheRequestedTimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var terminalEngagement = Engagement.CreateSlotSignUp(opportunityId, volunteerId, timeSlotId);
		terminalEngagement.Withdraw();
		_dbContext.GetTerminalEngagementAsync(volunteerId, opportunityId, timeSlotId, Arg.Any<CancellationToken>())
			.Returns(terminalEngagement);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeSameAs(terminalEngagement);
		result.Status.Should().Be(EngagementStatus.Pending);
		await _engagementRepo.DidNotReceive().AddAsync(Arg.Any<Engagement>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldCreateNewEngagement_WhenATerminalEngagementOnlyExistsForAnotherTimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert

		result.TimeSlotId.Should().Be(timeSlotId);
		result.Status.Should().Be(EngagementStatus.Pending);
		await _engagementRepo.Received(1).AddAsync(Arg.Any<Engagement>(), cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldReportADuplicateSlotSignUp_AsScopedToTheTimeSlot(
		CancellationToken cancellationToken)
	{
		// Arrange
		// The duplicate check matches on the time slot, so a second slot of the
		// same opportunity is fine - saying "this opportunity" was wrong (#2323).
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		_dbContext.HasEngagementAsync(volunteerId, opportunityId, timeSlotId, Arg.Any<CancellationToken>())
			.Returns(true);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, timeSlotId, Message: null);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Engagement.AlreadySignedUpForTimeSlot");
	}

	[Test]
	public async Task Handle_ShouldReportADuplicateInterestSignUp_AsScopedToTheOpportunity(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		var volunteerId = UserId.New();
		SetupOpportunityExists(opportunityId);
		_dbContext.HasEngagementAsync(volunteerId, opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(true);
		var command = new CreateEngagementCommand(opportunityId, volunteerId, TimeSlotId: null, "Ich helfe gerne!");

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Engagement.AlreadySignedUp");
	}

	[Test]
	public async Task Handle_ShouldNotSendAnyEmailSynchronously_RegardlessOfHowManyOrganizersExist(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = VolunteerOpportunityId.New();
		var timeSlotId = SetupOpportunityExistsWithTimeSlot(opportunityId);
		_keycloakService.GetMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns([
				new KeycloakOrganizationMember(Guid.NewGuid(), "olaf", "Olaf", "Organizer", "olaf@example.com", true),
				new KeycloakOrganizationMember(Guid.NewGuid(), "petra", "Petra", "Organizer", "petra@example.com", true),
			]);
		var command = new CreateEngagementCommand(opportunityId, UserId.New(), timeSlotId, Message: null);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert

		await _keycloakService.Received(1).GetMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}
}
