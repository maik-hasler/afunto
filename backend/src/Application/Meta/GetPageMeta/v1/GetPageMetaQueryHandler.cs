using Application.Common.Messaging;
using Application.Common.Meta;
using Application.Common.StaticPages;

namespace Application.Meta.GetPageMeta.v1;

/// <summary>
/// Crawler HTML for one of the SPA's static routes. The per-entity handlers
/// alongside this one already give opportunity and organization links their own
/// preview; every static route instead fell through to index.html, whose og:url
/// is hardcoded to the site root - so a /help link shared into Slack or
/// WhatsApp previewed as the homepage and declared the homepage canonical
/// (afunto#2331).
/// </summary>
internal sealed class GetPageMetaQueryHandler : IQueryHandler<GetPageMetaQuery, string?>
{
	public ValueTask<string?> Handle(
		GetPageMetaQuery request,
		CancellationToken cancellationToken = default)
	{
		var page = StaticPageCatalog.Find(request.Slug);

		if (page is null)
			return ValueTask.FromResult<string?>(null);

		var baseUrl = request.BaseUrl.TrimEnd('/');

		return ValueTask.FromResult<string?>(MetaHtmlBuilder.Build(
			page.Title,
			page.Description,
			$"{baseUrl}{page.Path}",
			$"{baseUrl}/og-image.png"));
	}
}
