using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Users.DeleteMyAccount.v1;
using AwesomeAssertions;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Users.DeleteMyAccount;

public class DeleteMyAccountCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<User, UserId> _usersRepo = Substitute.For<IAggregateRepository<User, UserId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly DeleteMyAccountCommandHandler _sut;

	private static readonly UserId DefaultUserId = UserId.New();

	public DeleteMyAccountCommandHandlerTests()
	{
		_dbContext.Users.Returns(_usersRepo);
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_dbContext
			.GetOrganizerOrganizationsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Organization>());
		_dbContext
			.GetReportHistoryForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());

		_dbContext
			.FindUserIncludingDeletedAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => User.Create(callInfo.ArgAt<UserId>(0)));
		_sut = new DeleteMyAccountCommandHandler(_dbContext, _fileStorage);
	}

	private static Engagement CreateEngagementFor(UserId volunteerId) =>
		Engagement.CreateSlotSignUp(VolunteerOpportunityId.New(), volunteerId, TimeSlotId.New());

	private static Organization CreateOrganization(string name) =>
		Organization.Create(OrganizationId.New(), name).Value;

	[Test]
	public async Task Handle_ShouldAnonymizeAllEngagements_ReturnedForVolunteerTracking(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagementOne = CreateEngagementFor(DefaultUserId);
		var engagementTwo = CreateEngagementFor(DefaultUserId);
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(DefaultUserId, cancellationToken)
			.Returns([engagementOne, engagementTwo]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		engagementOne.IsAnonymized.Should().BeTrue();
		engagementTwo.IsAnonymized.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldWithdrawPendingEngagement_BeforeAnonymizing_SoItStopsOccupyingCapacity(
		CancellationToken cancellationToken)
	{
		// Arrange

		var engagement = CreateEngagementFor(DefaultUserId);
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(DefaultUserId, cancellationToken)
			.Returns([engagement]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		engagement.Status.Should().Be(EngagementStatus.Withdrawn);
		engagement.IsAnonymized.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldWithdrawConfirmedNotCheckedInEngagement_BeforeAnonymizing(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagement = CreateEngagementFor(DefaultUserId);
		engagement.Confirm().ThrowIfFailure();
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(DefaultUserId, cancellationToken)
			.Returns([engagement]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		engagement.Status.Should().Be(EngagementStatus.Withdrawn);
		engagement.IsAnonymized.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldLeaveCheckedInEngagementConfirmed_ButStillAnonymizeIt(
		CancellationToken cancellationToken)
	{
		// Arrange

		var engagement = CreateEngagementFor(DefaultUserId);
		engagement.Confirm().ThrowIfFailure();
		engagement.CheckIn(DateTimeOffset.UtcNow).ThrowIfFailure();
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(DefaultUserId, cancellationToken)
			.Returns([engagement]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		engagement.Status.Should().Be(EngagementStatus.Confirmed);
		engagement.IsCheckedIn.Should().BeTrue();
		engagement.IsAnonymized.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldLeaveAlreadyTerminatedEngagementAsIs_ButStillAnonymizeIt(
		CancellationToken cancellationToken)
	{
		// Arrange
		var engagement = CreateEngagementFor(DefaultUserId);
		engagement.Cancel().ThrowIfFailure();
		_dbContext
			.GetEngagementsForVolunteerTrackingAsync(DefaultUserId, cancellationToken)
			.Returns([engagement]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		engagement.Status.Should().Be(EngagementStatus.Cancelled);
		engagement.IsAnonymized.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldDeleteNotificationsForRecipient(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).DeleteNotificationsForRecipientAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldDeleteAvatarByItsExactObjectKey_AndSwallowFailures(
		CancellationToken cancellationToken)
	{
		// Arrange
		var user = User.Create(DefaultUserId);
		user.SetAvatarUrl($"https://example.com/user-avatars/{DefaultUserId.Value}/abc123.png");
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		_fileStorage
			.GetObjectKeyFromPublicUrl($"https://example.com/user-avatars/{DefaultUserId.Value}/abc123.png")
			.Returns($"user-avatars/{DefaultUserId.Value}/abc123.png");
		_fileStorage
			.DeleteAsync($"user-avatars/{DefaultUserId.Value}/abc123.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();

		await _fileStorage.Received(1).DeleteAsync($"user-avatars/{DefaultUserId.Value}/abc123.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptAvatarDeletion_WhenUserHasNoAvatar(
		CancellationToken cancellationToken)
	{
		// Arrange
		var user = User.Create(DefaultUserId);
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAttemptAvatarDeletion_WhenStoredAvatarUrlDoesNotMatchAnyKnownObjectKey(
		CancellationToken cancellationToken)
	{
		// Arrange

		var user = User.Create(DefaultUserId);
		user.SetAvatarUrl("not-a-valid-storage-url");
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		_fileStorage.GetObjectKeyFromPublicUrl("not-a-valid-storage-url").Returns((string?)null);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDeleteTheUserRow_WhenUserExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var user = User.Create(DefaultUserId);
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		_usersRepo.Received(1).Delete(user);
	}

	[Test]
	public async Task Handle_ShouldThrowNotFound_WhenLocalUserRowIsMissing(
		CancellationToken cancellationToken)
	{
		// Arrange

		_dbContext
			.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken)
			.Returns((User?)null);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		_usersRepo.DidNotReceive().Delete(Arg.Any<User>());
	}

	[Test]
	public async Task Handle_ShouldDeleteTheUserRow_WhenUserIsShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange

		var user = User.Create(DefaultUserId);
		user.MarkDeleted(DateTimeOffset.UtcNow);
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		_usersRepo.Received(1).Delete(user);
		user.Events.Should().ContainSingle().Which.Should().BeOfType<UserAccountDeletedDomainEvent>();
	}

	[Test]
	public async Task Handle_ShouldRaiseUserAccountDeletedEvent_OnlyAfterUserRowIsFoundAndMarkedForDeletion(
		CancellationToken cancellationToken)
	{
		// Arrange

		var user = User.Create(DefaultUserId);
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		user.Events.Should().ContainSingle()
			.Which.Should().BeOfType<UserAccountDeletedDomainEvent>()
			.Which.UserId.Should().Be(DefaultUserId);
	}

	[Test]
	public async Task Handle_ShouldReturnTrue(
		CancellationToken cancellationToken)
	{
		// Arrange
		var user = User.Create(DefaultUserId);
		_dbContext.FindUserIncludingDeletedAsync(DefaultUserId, cancellationToken).Returns(user);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldDeleteTheUserStreak(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).DeleteUserStreakAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldDeleteAchievementsForTheUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).DeleteAchievementsForUserAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldRemoveOrganizationMembershipsAndDashboardLayoutsForTheUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).RemoveMembershipsForUserAsync(DefaultUserId, cancellationToken);
		await _dbContext.Received(1).RemoveDashboardLayoutsForUserAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldDeleteOrganizationInvitationsForTheUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).DeleteInvitationsForUserAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldDeleteReportsFiledByTheUser(
		CancellationToken cancellationToken)
	{
		// Arrange

		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.Received(1).DeleteReportsForReporterAsync(DefaultUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldStampTargetDeletedOn_ForReportsAgainstTheUser(
		CancellationToken cancellationToken)
	{
		// Arrange

		var report = Report.Create(
			ReportTargetType.User, DefaultUserId.Value, UserId.New(), ReportReason.Harassment, details: null).Value;
		_dbContext
			.GetReportHistoryForTargetAsync(ReportTargetType.User, DefaultUserId.Value, cancellationToken)
			.Returns([report]);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		report.TargetDeletedOn.Should().NotBeNull();
	}

	[Test]
	public async Task Handle_ShouldThrowConflict_AndPerformNoOtherAction_WhenSoleOrganizerOfAnOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		var organization = CreateOrganization("Solo Org");
		_dbContext
			.GetOrganizerOrganizationsAsync(DefaultUserId, cancellationToken)
			.Returns([organization]);
		_dbContext
			.CountOrganizersAsync(organization.Id, cancellationToken)
			.Returns(1);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		var thrown = await act.Should().ThrowAsync<ResultFailureException>();
		thrown.Which.Message.Should().Contain("Solo Org");
		await _dbContext.DidNotReceive().DeleteNotificationsForRecipientAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().RemoveMembershipsForUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().DeleteUserStreakAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().DeleteReportsForReporterAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().GetReportHistoryForTargetAsync(
			Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		_usersRepo.DidNotReceive().Delete(Arg.Any<User>());
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldListEveryBlockingOrganizationByName_WhenSoleOrganizerOfMultipleOrganizations(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgAlpha = CreateOrganization("Org Alpha");
		var orgBeta = CreateOrganization("Org Beta");
		_dbContext
			.GetOrganizerOrganizationsAsync(DefaultUserId, cancellationToken)
			.Returns([orgAlpha, orgBeta]);
		_dbContext.CountOrganizersAsync(orgAlpha.Id, cancellationToken).Returns(1);
		_dbContext.CountOrganizersAsync(orgBeta.Id, cancellationToken).Returns(1);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		var thrown = await act.Should().ThrowAsync<ResultFailureException>();
		thrown.Which.Message.Should().Contain("Org Alpha");
		thrown.Which.Message.Should().Contain("Org Beta");
	}

	[Test]
	public async Task Handle_ShouldProceed_WhenOrganizerButOtherOrganizersRemainForThatOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		var organization = CreateOrganization("Shared Org");
		_dbContext
			.GetOrganizerOrganizationsAsync(DefaultUserId, cancellationToken)
			.Returns([organization]);
		_dbContext
			.CountOrganizersAsync(organization.Id, cancellationToken)
			.Returns(2);
		var command = new DeleteMyAccountCommand(DefaultUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _dbContext.Received(1).RemoveMembershipsForUserAsync(DefaultUserId, cancellationToken);
	}
}
