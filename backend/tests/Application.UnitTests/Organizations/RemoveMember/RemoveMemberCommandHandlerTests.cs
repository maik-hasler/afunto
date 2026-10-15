using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Organizations.RemoveMember.v1;
using Application.UnitTests.Common.Time;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Organizations.RemoveMember;

public class RemoveMemberCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IKeycloakOrganizationService _keycloakService = Substitute.For<IKeycloakOrganizationService>();
	private readonly RemoveMemberCommandHandler _sut;

	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

	public RemoveMemberCommandHandlerTests()
	{
		_dbContext
			.GetOrganizerOrganizationsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Organization>());
		_sut = new RemoveMemberCommandHandler(_dbContext, _keycloakService, new FakeTimeProvider(Now));
	}

	private void AllowRequestingUserInOrg(Guid orgId)
	{
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		_dbContext.IsOrganizerAsync(organizationId, DefaultRequestingUserId, Arg.Any<CancellationToken>()).Returns(true);
		_dbContext.IsMemberAsync(organizationId, DefaultRequestingUserId, Arg.Any<CancellationToken>()).Returns(true);
	}

	private void AllowRequestingUserAsPlainMember(Guid orgId)
	{
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		_dbContext.IsOrganizerAsync(organizationId, DefaultRequestingUserId, Arg.Any<CancellationToken>()).Returns(false);
		_dbContext.IsMemberAsync(organizationId, DefaultRequestingUserId, Arg.Any<CancellationToken>()).Returns(true);
	}

	private void SetTargetIsOrganizer(Guid orgId, Guid targetUserId, bool isOrganizer) =>
		_dbContext
			.IsOrganizerAsync(OrganizationId.Create(orgId).GetValueOrThrow(), UserId.Create(targetUserId).GetValueOrThrow(), Arg.Any<CancellationToken>())
			.Returns(isOrganizer);

	private void SetOrganizerCount(Guid orgId, int count) =>
		_dbContext
			.CountOrganizersAsync(OrganizationId.Create(orgId).GetValueOrThrow(), Arg.Any<CancellationToken>())
			.Returns(count);

	private User TrackUser(Guid userId)
	{
		var id = UserId.Create(userId).GetValueOrThrow();
		var user = User.Create(id);
		_dbContext.GetOrCreateUserAsync(id, Arg.Any<CancellationToken>()).Returns(user);
		return user;
	}

	private void SetRemainingOrganizerOrganizations(Guid userId, params Organization[] organizations) =>
		_dbContext
			.GetOrganizerOrganizationsAsync(UserId.Create(userId).GetValueOrThrow(), Arg.Any<CancellationToken>())
			.Returns(organizations.ToList());

	[Test]
	public async Task Handle_ShouldCallRemoveMemberOnKeycloak(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _keycloakService.Received(1).RemoveMemberAsync(orgId, userId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldReturnTrue_OnSuccess(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldPropagateException_WhenKeycloakFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		_keycloakService
			.RemoveMemberAsync(orgId, userId, cancellationToken)
			.ThrowsAsync(new HttpRequestException("Keycloak responded with 404 NotFound"));

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<HttpRequestException>()
			.WithMessage("*404*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotAMemberOfTheOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		_dbContext
			.IsOrganizerAsync(OrganizationId.Create(orgId).GetValueOrThrow(), DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _keycloakService.DidNotReceive().RemoveMemberAsync(orgId, userId, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRemovingTheLastRemainingOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetOrganizerCount(orgId, 1);
		var command = new RemoveMemberCommand(orgId, DefaultRequestingUserId.Value, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*only organizer*");
		await _keycloakService.DidNotReceive().RemoveMemberAsync(orgId, DefaultRequestingUserId.Value, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAllowRemoval_WhenTargetIsNotAnOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var otherUserId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, otherUserId, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, otherUserId, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).RemoveMemberAsync(orgId, otherUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldAllowSelfRemoval_WhenRequestingUserIsOnlyAPlainMember(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		AllowRequestingUserAsPlainMember(orgId);
		SetTargetIsOrganizer(orgId, DefaultRequestingUserId.Value, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, DefaultRequestingUserId.Value, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).RemoveMemberAsync(orgId, DefaultRequestingUserId.Value, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenPlainMemberTriesToRemoveSomeoneElse(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var otherUserId = Guid.NewGuid();
		AllowRequestingUserAsPlainMember(orgId);
		var command = new RemoveMemberCommand(orgId, otherUserId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _keycloakService.DidNotReceive().RemoveMemberAsync(orgId, otherUserId, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAllowRemoval_WhenAnotherOrganizerRemains(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetOrganizerCount(orgId, 2);
		var command = new RemoveMemberCommand(orgId, DefaultRequestingUserId.Value, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).RemoveMemberAsync(orgId, DefaultRequestingUserId.Value, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldRevokeKeycloakRole_WhenRemovedOrganizerHasNoRemainingOrganizations(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: true);
		SetOrganizerCount(orgId, 2);
		SetRemainingOrganizerOrganizations(userId);
		var removedUser = TrackUser(userId);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _keycloakService.Received(1).RevokeOrganizerRoleAsync(userId, cancellationToken);
		removedUser.PermissionsChangedOn.Should().Be(Now);
	}

	[Test]
	public async Task Handle_ShouldNotRevokeKeycloakRole_WhenRemovedOrganizerStillOrganizesAnotherOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: true);
		SetOrganizerCount(orgId, 2);
		var otherOrg = Organization.Create(OrganizationId.New(), "Other Org").GetValueOrThrow();
		SetRemainingOrganizerOrganizations(userId, otherOrg);
		var removedUser = TrackUser(userId);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _keycloakService.DidNotReceive().RevokeOrganizerRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		removedUser.PermissionsChangedOn.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldNotRevokeKeycloakRole_WhenRemovedMemberWasNotAnOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		AllowRequestingUserInOrg(orgId);
		SetTargetIsOrganizer(orgId, userId, isOrganizer: false);
		var command = new RemoveMemberCommand(orgId, userId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _dbContext.DidNotReceive().GetOrganizerOrganizationsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
		await _keycloakService.DidNotReceive().RevokeOrganizerRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}
}
