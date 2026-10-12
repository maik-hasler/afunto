using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Organizations.ResendInvitation.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Organizations.ResendInvitation;

public class ResendInvitationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IAggregateRepository<OrganizationInvitation, OrganizationInvitationId> _invitationRepo =
		Substitute.For<IAggregateRepository<OrganizationInvitation, OrganizationInvitationId>>();
	private readonly ResendInvitationCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static readonly UserId DefaultInviteeId = UserId.New();

	public ResendInvitationCommandHandlerTests()
	{
		_dbContext.OrganizationInvitations.Returns(_invitationRepo);
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(true);
		_sut = new ResendInvitationCommandHandler(_dbContext, _unitOfWork);
	}

	private static OrganizationInvitation CreateExpiredInvitation(OrganizationId orgId)
	{
		var now = DateTimeOffset.UtcNow;
		var invitation = OrganizationInvitation.Create(
			orgId, DefaultInviteeId, UserId.New(), OrganizationMemberRole.Organizer, now);
		invitation.Expire(now.AddDays(OrganizationInvitation.ExpiryWindowDays)).ThrowIfFailure();
		return invitation;
	}

	private static OrganizationInvitation CreatePendingInvitation(OrganizationId orgId) =>
		OrganizationInvitation.Create(
			orgId, DefaultInviteeId, UserId.New(), OrganizationMemberRole.Organizer, DateTimeOffset.UtcNow);

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotMemberOfTheOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultRequestingUserId, cancellationToken)
			.Returns(false);
		var invitation = CreateExpiredInvitation(DefaultOrgId);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*permission*");
		await _invitationRepo.DidNotReceive().FindAsync(Arg.Any<OrganizationInvitationId>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldResetToPending_WhenInvitationIsExpired(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreateExpiredInvitation(DefaultOrgId);
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		invitation.Status.Should().Be(InvitationStatus.Pending);
		invitation.ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(OrganizationInvitation.ExpiryWindowDays - 1));
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenInvitationDoesNotExist(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitationId = OrganizationInvitationId.New();
		_invitationRepo.FindAsync(invitationId, cancellationToken).Returns((OrganizationInvitation?)null);
		var command = new ResendInvitationCommand(DefaultOrgId, invitationId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenInvitationBelongsToADifferentOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		var otherOrgId = OrganizationId.New();
		var invitation = CreateExpiredInvitation(otherOrgId);
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Validation);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldExtendExpiry_WhenInvitationIsStillPending(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreatePendingInvitation(DefaultOrgId);
		var originalExpiry = invitation.ExpiresOn;
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		invitation.Status.Should().Be(InvitationStatus.Pending);
		invitation.ExpiresOn.Should().BeOnOrAfter(originalExpiry);
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Handle_ShouldThrowConflict_WhenInvitationIsAlreadyAnswered(
		bool accepted,
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreatePendingInvitation(DefaultOrgId);
		if (accepted)
			invitation.Accept().ThrowIfFailure();
		else
			invitation.Decline().ThrowIfFailure();
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}
}
