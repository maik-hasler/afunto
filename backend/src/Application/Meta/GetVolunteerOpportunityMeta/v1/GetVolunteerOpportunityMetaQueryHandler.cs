using Application.Common.Messaging;
using Application.Common.Meta;
using Application.VolunteerOpportunities;

namespace Application.Meta.GetVolunteerOpportunityMeta.v1;

internal sealed class GetVolunteerOpportunityMetaQueryHandler(
	IVolunteerOpportunityReadRepository readRepository)
	: IQueryHandler<GetVolunteerOpportunityMetaQuery, string?>
{
	public async ValueTask<string?> Handle(
		GetVolunteerOpportunityMetaQuery request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await readRepository.GetDetailsAsync(
			request.OpportunityId, requestingUserId: null, cancellationToken);

		if (opportunity is null)
			return null;

		var baseUrl = request.BaseUrl.TrimEnd('/');

		return MetaHtmlBuilder.Build(
			$"{opportunity.TitleDe} - Afunto",
			opportunity.DescriptionDe,
			$"{baseUrl}/volunteer-opportunities/{opportunity.Id}",
			opportunity.BannerImageUrl ?? $"{baseUrl}/og-image.png");
	}
}
