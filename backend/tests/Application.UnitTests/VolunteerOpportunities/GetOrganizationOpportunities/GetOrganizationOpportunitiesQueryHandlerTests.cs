using Application.Common.Exceptions;
using Application.Common.Pagination;
using Application.Common.Persistence;
using Application.VolunteerOpportunities;
using Application.VolunteerOpportunities.GetOrganizationOpportunities.v1;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.GetOrganizationOpportunities;

public class GetOrganizationOpportunitiesQueryHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IVolunteerOpportunityReadRepository _readRepository = Substitute.For<IVolunteerOpportunityReadRepository>();
	private readonly GetOrganizationOpportunitiesQueryHandler _sut;

	private static readonly Guid DefaultOrgId = Guid.NewGuid();
	private static readonly UserId DefaultRequestingUserId = UserId.New();

	public GetOrganizationOpportunitiesQueryHandlerTests()
	{
		_dbContext
			.IsMemberAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_readRepository
			.GetPagedSummariesByOrganizationAsync(Arg.Any<Guid>(), Arg.Any<OpportunityStatus>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
			.Returns(new PagedList<VolunteerOpportunitySummary>([], 0, 1, 10));
		_sut = new GetOrganizationOpportunitiesQueryHandler(_readRepository, _dbContext);
	}

	private async Task<(OpportunityStatus Status, int PageNumber, int PageSize)> CapturedArgsAsync(
		OpportunityStatus status, int pageNumber, int pageSize)
	{
		var capturedStatus = OpportunityStatus.Draft;
		var capturedPageNumber = 0;
		var capturedPageSize = 0;
		_readRepository
			.GetPagedSummariesByOrganizationAsync(
				Arg.Any<Guid>(),
				Arg.Do<OpportunityStatus>(s => capturedStatus = s),
				Arg.Do<int>(p => capturedPageNumber = p),
				Arg.Do<int>(s => capturedPageSize = s),
				Arg.Any<bool?>(),
				Arg.Any<CancellationToken>())
			.Returns(new PagedList<VolunteerOpportunitySummary>([], 0, 1, 10));

		await _sut.Handle(new GetOrganizationOpportunitiesQuery(DefaultOrgId, DefaultRequestingUserId, status, pageNumber, pageSize), CancellationToken.None);

		return (capturedStatus, capturedPageNumber, capturedPageSize);
	}

	[Test]
	public async Task Handle_ShouldPassThroughStatus_WhenDraft()
	{
		var (status, _, _) = await CapturedArgsAsync(OpportunityStatus.Draft, pageNumber: 1, pageSize: 10);
		status.Should().Be(OpportunityStatus.Draft);
	}

	[Test]
	public async Task Handle_ShouldPassThroughStatus_WhenPublished()
	{
		var (status, _, _) = await CapturedArgsAsync(OpportunityStatus.Published, pageNumber: 1, pageSize: 10);
		status.Should().Be(OpportunityStatus.Published);
	}

	[Test]
	public async Task Handle_ShouldClampZeroPageNumber_ToOne()
	{
		var (_, pageNumber, _) = await CapturedArgsAsync(OpportunityStatus.Published, pageNumber: 0, pageSize: 10);
		pageNumber.Should().Be(1);
	}

	[Test]
	public async Task Handle_ShouldClampNegativePageNumber_ToOne()
	{
		var (_, pageNumber, _) = await CapturedArgsAsync(OpportunityStatus.Published, pageNumber: -5, pageSize: 10);
		pageNumber.Should().Be(1);
	}

	[Test]
	public async Task Handle_ShouldClampZeroPageSize_ToOne()
	{
		var (_, _, pageSize) = await CapturedArgsAsync(OpportunityStatus.Published, pageNumber: 1, pageSize: 0);
		pageSize.Should().Be(1);
	}

	[Test]
	public async Task Handle_ShouldCapExcessivePageSize_ToHundred()
	{
		var (_, _, pageSize) = await CapturedArgsAsync(OpportunityStatus.Published, pageNumber: 1, pageSize: 5000);
		pageSize.Should().Be(100);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotAMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		_dbContext
			.IsMemberAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);

		var query = new GetOrganizationOpportunitiesQuery(DefaultOrgId, DefaultRequestingUserId, OpportunityStatus.Published, 1, 10);

		// Act
		Func<Task> act = async () => await _sut.Handle(query, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		await _readRepository.DidNotReceive().GetPagedSummariesByOrganizationAsync(
			Arg.Any<Guid>(), Arg.Any<OpportunityStatus>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
	}
}
