using Application.Common.Messaging;

namespace Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;

internal sealed class GetVolunteerOpportunityDateAvailabilityQueryHandler(
	IVolunteerOpportunityReadRepository readRepository)
	: IQueryHandler<GetVolunteerOpportunityDateAvailabilityQuery, IReadOnlyList<VolunteerOpportunityAvailableDate>>
{
	public async ValueTask<IReadOnlyList<VolunteerOpportunityAvailableDate>> Handle(
		GetVolunteerOpportunityDateAvailabilityQuery request,
		CancellationToken cancellationToken = default)
	{
		var filter = new VolunteerOpportunityDateAvailabilityFilter(
			request.From,
			request.To,
			request.Timezone,
			request.Occurrence,
			request.ParticipationType,
			request.IsRemote,
			request.CenterLatitude,
			request.CenterLongitude,
			request.RadiusKm,
			request.Categories,
			request.Tag,
			request.Keyword,
			request.RequestingUserId);

		return await readRepository.GetDateAvailabilityAsync(filter, cancellationToken);
	}
}
