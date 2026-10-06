using System.Xml.Linq;
using Application.Common.Sitemap;
using Application.Common.StaticPages;
using Application.Organizations;
using Application.Sitemap.GetSitemap.v1;
using Application.VolunteerOpportunities;
using AwesomeAssertions;
using NSubstitute;

namespace Application.UnitTests.Sitemap.GetSitemap;

public class GetSitemapQueryHandlerTests
{
	private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

	private readonly IVolunteerOpportunityReadRepository _opportunityReadRepository =
		Substitute.For<IVolunteerOpportunityReadRepository>();
	private readonly IOrganizationReadRepository _organizationReadRepository =
		Substitute.For<IOrganizationReadRepository>();
	private readonly GetSitemapQueryHandler _sut;

	public GetSitemapQueryHandlerTests()
	{
		_opportunityReadRepository
			.GetPublishedForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([]);
		_organizationReadRepository
			.GetAllForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([]);
		_sut = new GetSitemapQueryHandler(_opportunityReadRepository, _organizationReadRepository);
	}

	[Test]
	public async Task Handle_ShouldListOnlyTheStaticPages_WhenNoOpportunitiesOrOrganizationsExist(
		CancellationToken cancellationToken)
	{
		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		var document = XDocument.Parse(xml);
		document.Root!.Name.Should().Be(SitemapNs + "urlset");
		Locations(document).Should().BeEquivalentTo(
			StaticPageCatalog.All.Select(page => $"https://afunto.example{page.Path}"));
	}

	[Test]
	public async Task Handle_ShouldIncludeTheSiteRoot(CancellationToken cancellationToken)
	{
		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		Locations(XDocument.Parse(xml)).Should().Contain("https://afunto.example/");
	}

	[Test]
	[Arguments("/opportunities")]
	[Arguments("/organizations")]
	[Arguments("/help")]
	[Arguments("/contact")]
	[Arguments("/imprint")]
	[Arguments("/privacy-policy")]
	[Arguments("/terms-of-use")]
	public async Task Handle_ShouldIncludeEveryStaticPage(
		string path,
		CancellationToken cancellationToken)
	{
		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		Locations(XDocument.Parse(xml)).Should().Contain($"https://afunto.example{path}");
	}

	[Test]
	public async Task Handle_ShouldOmitLastModified_ForStaticPages(
		CancellationToken cancellationToken)
	{
		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		var root = XDocument.Parse(xml).Root!;
		var staticEntry = root.Elements(SitemapNs + "url")
			.Single(u => u.Element(SitemapNs + "loc")!.Value == "https://afunto.example/help");

		staticEntry.Element(SitemapNs + "lastmod").Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldIncludeOneUrlEntry_PerOrganizationAndOpportunity(
		CancellationToken cancellationToken)
	{
		var orgId = Guid.NewGuid();
		var opportunityId = Guid.NewGuid();
		_organizationReadRepository
			.GetAllForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([new SitemapEntry(orgId, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))]);
		_opportunityReadRepository
			.GetPublishedForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([new SitemapEntry(opportunityId, new DateTimeOffset(2026, 2, 20, 0, 0, 0, TimeSpan.Zero))]);

		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		var document = XDocument.Parse(xml);
		var locs = document.Root!.Elements(SitemapNs + "url")
			.Select(u => u.Element(SitemapNs + "loc")!.Value)
			.ToList();

		locs.Should().Contain([
			$"https://afunto.example/organizations/{orgId}",
			$"https://afunto.example/volunteer-opportunities/{opportunityId}",
		]);
		locs.Should().HaveCount(StaticPageCatalog.All.Count + 2);
	}

	[Test]
	public async Task Handle_ShouldTrimTrailingSlash_FromBaseUrl(
		CancellationToken cancellationToken)
	{
		var orgId = Guid.NewGuid();
		_organizationReadRepository
			.GetAllForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([new SitemapEntry(orgId, DateTimeOffset.UtcNow)]);

		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example/"), cancellationToken);

		Locations(XDocument.Parse(xml)).Should()
			.Contain($"https://afunto.example/organizations/{orgId}");
	}

	[Test]
	public async Task Handle_ShouldFormatLastModified_AsDateOnly(
		CancellationToken cancellationToken)
	{
		var orgId = Guid.NewGuid();
		_organizationReadRepository
			.GetAllForSitemapAsync(Arg.Any<CancellationToken>())
			.Returns([new SitemapEntry(orgId, new DateTimeOffset(2026, 3, 7, 13, 45, 0, TimeSpan.Zero))]);

		var xml = await _sut.Handle(new GetSitemapQuery("https://afunto.example"), cancellationToken);

		var root = XDocument.Parse(xml).Root!;
		var entry = root.Elements(SitemapNs + "url")
			.Single(u => u.Element(SitemapNs + "loc")!.Value
				== $"https://afunto.example/organizations/{orgId}");

		entry.Element(SitemapNs + "lastmod")!.Value.Should().Be("2026-03-07");
	}

	private static List<string> Locations(XDocument document) =>
		document.Root!.Elements(SitemapNs + "url")
			.Select(u => u.Element(SitemapNs + "loc")!.Value)
			.ToList();
}
