using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.AssignMemberToOpportunity.v1;
using Application.Engagements.CreateEngagement.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.Engagements.AssignMemberToOpportunity;

public class AssignMemberToOpportunityCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly ISender _sender = Substitute.For<ISender>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly AssignMemberToOpportunityCommandHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;

	public AssignMemberToOpportunityCommandHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_sut = new AssignMemberToOpportunityCommandHandler(_dbContext, _sender, TimeProvider.System);
	}

	private VolunteerOpportunity SetupOpportunity()
	{
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(), "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.IndividualContact,
			CheckInMethod.None, Substitute.For<IPinGenerator>(), status: OpportunityStatus.Published,
			validUntil: DateTimeOffset.UtcNow.AddDays(30), membersOnly: true).Value;
		_opportunityRepo.FindAsync(opportunity.Id, Arg.Any<CancellationToken>()).Returns(opportunity);
		return opportunity;
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequesterIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = SetupOpportunity();
		_dbContext.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new AssignMemberToOpportunityCommand(opportunity.Id, UserId.New(), null, null, UserId.New());

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		await _sender.DidNotReceive().Send(Arg.Any<CreateEngagementCommand>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenAssigneeIsNotMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = SetupOpportunity();
		_dbContext.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_dbContext.IsMemberAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new AssignMemberToOpportunityCommand(opportunity.Id, UserId.New(), null, null, UserId.New());

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Engagement.AssigneeNotMember");
		await _sender.DidNotReceive().Send(Arg.Any<CreateEngagementCommand>(), Arg.Any<CancellationToken>());
	}
}
