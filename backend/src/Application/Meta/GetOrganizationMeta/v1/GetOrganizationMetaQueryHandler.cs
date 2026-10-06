using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Meta;
using Application.Common.Persistence;
using Domain.Organizations;

namespace Application.Meta.GetOrganizationMeta.v1;

internal sealed class GetOrganizationMetaQueryHandler(IApplicationDbContext dbContext)
	: IQueryHandler<GetOrganizationMetaQuery, string?>
{
	public async ValueTask<string?> Handle(
		GetOrganizationMetaQuery request,
		CancellationToken cancellationToken = default)
	{
		var organization = await dbContext.Organizations.FindAsync(
			OrganizationId.Create(request.OrganizationId).GetValueOrThrow(), cancellationToken);

		if (organization is null)
			return null;

		var baseUrl = request.BaseUrl.TrimEnd('/');

		return MetaHtmlBuilder.Build(
			$"{organization.Name} - Afunto",
			organization.Description,
			$"{baseUrl}/organizations/{organization.Id.Value}",
			organization.LogoUrl ?? $"{baseUrl}/og-image.png");
	}
}
