using System.Text;
using Application.Common.Messaging;
using Application.Common.Sitemap;
using Application.Common.StaticPages;
using Application.Organizations;
using Application.VolunteerOpportunities;

namespace Application.Sitemap.GetSitemap.v1;

internal sealed class GetSitemapQueryHandler(
	IVolunteerOpportunityReadRepository opportunityReadRepository,
	IOrganizationReadRepository organizationReadRepository)
	: IQueryHandler<GetSitemapQuery, string>
{
	public async ValueTask<string> Handle(
		GetSitemapQuery request,
		CancellationToken cancellationToken = default)
	{
		var opportunities = await opportunityReadRepository.GetPublishedForSitemapAsync(cancellationToken);
		var organizations = await organizationReadRepository.GetAllForSitemapAsync(cancellationToken);

		var baseUrl = request.BaseUrl.TrimEnd('/');

		var sb = new StringBuilder();
		sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
		sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

		// robots.txt points crawlers straight here, so a page missing from this
		// document is a page they have no reason to look for. The static routes
		// were absent entirely - the site root included (afunto#2331).
		// They carry no <lastmod>: it is optional per the sitemap protocol, and
		// these pages change with a release, not on a date the database knows.
		foreach (var page in StaticPageCatalog.All)
			AppendUrl(sb, $"{baseUrl}{page.Path}", lastModified: null);

		foreach (var entry in organizations)
			AppendUrl(sb, $"{baseUrl}/organizations/{entry.Id}", entry.LastModified);

		foreach (var entry in opportunities)
			AppendUrl(sb, $"{baseUrl}/volunteer-opportunities/{entry.Id}", entry.LastModified);

		sb.AppendLine("</urlset>");

		return sb.ToString();
	}

	private static void AppendUrl(StringBuilder sb, string loc, DateTimeOffset? lastModified)
	{
		sb.AppendLine("\t<url>");
		sb.AppendLine($"\t\t<loc>{loc}</loc>");
		if (lastModified is { } modified)
			sb.AppendLine($"\t\t<lastmod>{modified.UtcDateTime:yyyy-MM-dd}</lastmod>");
		sb.AppendLine("\t</url>");
	}
}
