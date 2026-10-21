using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.CreateVolunteerOpportunity.v1;

internal sealed class CreateVolunteerOpportunityCommandHandler(
	IApplicationDbContext dbContext,
	IPinGenerator pinGenerator)
	: ICommandHandler<CreateVolunteerOpportunityCommand, VolunteerOpportunity>
{
	public async ValueTask<VolunteerOpportunity> Handle(
		CreateVolunteerOpportunityCommand request,
		CancellationToken cancellationToken = default)
	{
		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			request.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		var opportunity = VolunteerOpportunity.Create(
			request.OrganizationId,
			request.TitleDe,
			request.TitleEn,
			request.DescriptionDe,
			request.DescriptionEn,
			request.IsRemote,
			request.Address,
			request.Occurrence,
			request.ParticipationType,
			request.CheckInMethod,
			pinGenerator,
			request.Category,
			request.Tags,
			request.Status,
			request.CheckInPin,
			request.ValidUntil,
			membersOnly: request.MembersOnly).GetValueOrThrow();

		await dbContext.VolunteerOpportunities.AddAsync(opportunity, cancellationToken);

		return opportunity;
	}
}
