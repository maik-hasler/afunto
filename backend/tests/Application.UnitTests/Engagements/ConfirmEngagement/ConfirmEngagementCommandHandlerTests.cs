using Application.Achievements.AwardAchievement.v1;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.ConfirmEngagement.v1;
using Application.UnitTests.Common.Time;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.Engagements.ConfirmEngagement;

public class ConfirmEngagementCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Engagement, EngagementId> _engagementRepo =
		Substitute.For<IAggregateRepository<Engagement, EngagementId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IAggregateRepository<UserStreak, UserStreakId> _streakRepo =
		Substitute.For<IAggregateRepository<UserStreak, UserStreakId>>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly ISender _sender = Substitute.For<ISender>();
	private readonly ConfirmEngagementCommandHandler _sut;

	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly Address DefaultAddress = Address.Create("Teststraße", "1", "12345", "Berlin").Value;

	public ConfirmEngagementCommandHandlerTests()
	{
		_dbContext.Engagements.Returns(_engagementRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext.UserStreaks.Returns(_streakRepo);
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_opportunityRepo
			.FindAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(CreateDefaultOpportunity());
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_dbContext
			.GetOrCreateUserStreakAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => UserStreak.Create(callInfo.Arg<UserId>()));
		_sut = new ConfirmEngagementCommandHandler(_dbContext, _sender, TimeProvider.System);
	}

	[Test]
	public async Task Handle_ShouldConfirmEngagement_WhenEngagementIsPending(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementId = EngagementId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			UserId.New(),
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var command = new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Status.Should().Be(EngagementStatus.Confirmed);
	}

	[Test]
	public async Task Handle_ShouldReturnEngagement_WithCorrectId(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementId = EngagementId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			UserId.New(),
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var command = new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeSameAs(engagement);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenEngagementNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementId = EngagementId.New();
		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns((Engagement?)null);

		var command = new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage($"*{engagementId.Value}*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenEngagementIsAlreadyConfirmed(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementId = EngagementId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			UserId.New(),
			TimeSlotId.New());
		engagement.Confirm();

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var command = new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*Only pending*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenEngagementIsCancelled(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementId = EngagementId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			UserId.New(),
			TimeSlotId.New());
		engagement.Cancel();

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var command = new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*Only pending*");
	}

	[Test]
	public async Task Handle_ShouldAwardWeeklyHeroBadge_WhenActivityStreakReaches4(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var streak = BuildActivityStreakOf(volunteerId, 3);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.Received(1).Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "weekly-hero-4" && c.UserId == volunteerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAwardWeeklyHeroBadge_WhenActivityStreakIsBelow3(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var streak = BuildActivityStreakOf(volunteerId, 1);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.DidNotReceive().Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "weekly-hero-4"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAwardFirstStepBadge_WhenVolunteerHasNoPriorStreak(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(UserStreak.Create(volunteerId));

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.Received(1).Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "first-step" && c.UserId == volunteerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAwardDedicatedBadge_WhenLifetimeConfirmationsReach5_EvenIfLiveConfirmedCountIsLower(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var streak = BuildStreakWithTotalConfirmedEngagementsOf(volunteerId, 4);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.Received(1).Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "dedicated-5" && c.UserId == volunteerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAwardDedicatedBadge_WhenLifetimeConfirmationsAreBelow5(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var streak = BuildStreakWithTotalConfirmedEngagementsOf(volunteerId, 2);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.DidNotReceive().Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "dedicated-5"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAwardCenturionBadge_WhenLifetimeConfirmationsReach100(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.New(),
			volunteerId,
			TimeSlotId.New());

		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		var streak = BuildStreakWithTotalConfirmedEngagementsOf(volunteerId, 99);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		await _sender.Received(1).Send(
			Arg.Is<AwardAchievementCommand>(c => c!.BadgeKey == "centurion-100" && c.UserId == volunteerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRaiseConfirmedEvent_ForThePostCommitEmailHandler(
		CancellationToken cancellationToken)
	{
		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(VolunteerOpportunityId.New(), volunteerId, TimeSlotId.New());
		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);

		await _sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		engagement.Events.Should().ContainSingle(e => e is EngagementConfirmedDomainEvent);
	}

	[Test]
	public async Task Handle_ShouldBucketActivityByTheBerlinIsoWeek_NotAWallClockDayEarlierInUtc(
		CancellationToken cancellationToken)
	{
		// 2026-11-01 23:30 UTC is still Sunday in UTC (ISO week 44) but is already
		// Monday 2026-11-02 00:30 in Europe/Berlin (CET, UTC+1) - the start of ISO
		// week 45. There is no longer any per-request timezone input that could
		// move this result (#2203) - it must always resolve via Berlin.
		var pinnedNow = new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero);
		var sut = new ConfirmEngagementCommandHandler(_dbContext, _sender, new FakeTimeProvider(pinnedNow));

		var engagementId = EngagementId.New();
		var volunteerId = UserId.New();
		var engagement = Engagement.CreateSlotSignUp(VolunteerOpportunityId.New(), volunteerId, TimeSlotId.New());
		_engagementRepo.FindAsync(engagementId, cancellationToken).Returns(engagement);
		var streak = UserStreak.Create(volunteerId);
		_dbContext.GetOrCreateUserStreakAsync(volunteerId, cancellationToken).Returns(streak);

		await sut.Handle(new ConfirmEngagementCommand(engagementId, DefaultRequestingUserId), cancellationToken);

		streak.LastActiveIsoYear.Should().Be(2026);
		streak.LastActiveIsoWeek.Should().Be(45);
	}

	private VolunteerOpportunity CreateDefaultOpportunity() =>
		VolunteerOpportunity.Create(DefaultOrgId, "Test", null, "Test", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;

	private static UserStreak BuildStreakWithTotalConfirmedEngagementsOf(UserId userId, int count)
	{
		var streak = UserStreak.Create(userId);
		for (var i = 0; i < count; i++)
			streak.RecordConfirmedEngagement();
		return streak;
	}

	private static UserStreak BuildActivityStreakOf(UserId userId, int weeks)
	{
		var streak = UserStreak.Create(userId);

		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, berlin).DateTime;
		var currentYear = System.Globalization.ISOWeek.GetYear(now);
		var currentWeek = System.Globalization.ISOWeek.GetWeekOfYear(now);
		for (var i = weeks; i >= 1; i--)
		{
			var week = currentWeek - i;
			var year = currentYear;
			if (week <= 0)
			{
				year--;
				week += 52;
			}
			streak.RecordActivity(year, week);
		}
		return streak;
	}
}
