using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Meta.GetOrganizationMeta.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using NSubstitute;

namespace Application.UnitTests.Meta.GetOrganizationMeta;

public class GetOrganizationMetaQueryHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Organization, OrganizationId> _organizationRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
	private readonly GetOrganizationMetaQueryHandler _sut;

	public GetOrganizationMetaQueryHandlerTests()
	{
		_dbContext.Organizations.Returns(_organizationRepo);
		_sut = new GetOrganizationMetaQueryHandler(_dbContext);
	}

	private static Organization CreateOrganization(
		OrganizationId id,
		string name = "Küstenschutz e.V.",
		string? description = "Wir schützen die Küste.",
		string? logoUrl = null)
	{
		var organization = Organization.Create(id, name).Value;
		organization.ChangeDescription(description);
		organization.SetLogoUrl(logoUrl);
		return organization;
	}

	[Test]
	public async Task Handle_ShouldReturnNull_WhenOrganizationNotFound(CancellationToken cancellationToken)
	{
		var organizationId = Guid.NewGuid();
		_organizationRepo
			.FindAsync(OrganizationId.Create(organizationId).GetValueOrThrow(), cancellationToken)
			.Returns((Organization?)null);

		var result = await _sut.Handle(
			new GetOrganizationMetaQuery(organizationId, "https://afunto.example"),
			cancellationToken);

		result.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldIncludeNameDescriptionAndCanonicalUrl_WhenOrganizationFound(
		CancellationToken cancellationToken)
	{
		var organizationId = Guid.NewGuid();
		var domainId = OrganizationId.Create(organizationId).GetValueOrThrow();
		_organizationRepo
			.FindAsync(domainId, cancellationToken)
			.Returns(CreateOrganization(domainId));

		var html = await _sut.Handle(
			new GetOrganizationMetaQuery(organizationId, "https://afunto.example/"),
			cancellationToken);

		html.Should().NotBeNull();
		html.Should().Contain("Küstenschutz e.V. - Afunto");
		html.Should().Contain("Wir schützen die Küste.");
		html.Should().Contain($"https://afunto.example/organizations/{organizationId}");
	}

	[Test]
	public async Task Handle_ShouldFallBackToSiteOgImage_WhenOrganizationHasNoLogo(
		CancellationToken cancellationToken)
	{
		var organizationId = Guid.NewGuid();
		var domainId = OrganizationId.Create(organizationId).GetValueOrThrow();
		_organizationRepo
			.FindAsync(domainId, cancellationToken)
			.Returns(CreateOrganization(domainId, logoUrl: null));

		var html = await _sut.Handle(
			new GetOrganizationMetaQuery(organizationId, "https://afunto.example"),
			cancellationToken);

		html.Should().Contain("https://afunto.example/og-image.png");
	}

	[Test]
	public async Task Handle_ShouldUseOrganizationLogo_WhenSet(CancellationToken cancellationToken)
	{
		var organizationId = Guid.NewGuid();
		var domainId = OrganizationId.Create(organizationId).GetValueOrThrow();
		_organizationRepo
			.FindAsync(domainId, cancellationToken)
			.Returns(CreateOrganization(domainId, logoUrl: "https://storage.example/logos/abc.png"));

		var html = await _sut.Handle(
			new GetOrganizationMetaQuery(organizationId, "https://afunto.example"),
			cancellationToken);

		html.Should().Contain("https://storage.example/logos/abc.png");
		html.Should().NotContain("og-image.png");
	}

	[Test]
	public async Task Handle_ShouldHtmlEncodeOrganizationName_ToPreventMarkupInjection(
		CancellationToken cancellationToken)
	{
		var organizationId = Guid.NewGuid();
		var domainId = OrganizationId.Create(organizationId).GetValueOrThrow();
		_organizationRepo
			.FindAsync(domainId, cancellationToken)
			.Returns(CreateOrganization(domainId, name: "<script>alert(1)</script> & Friends"));

		var html = await _sut.Handle(
			new GetOrganizationMetaQuery(organizationId, "https://afunto.example"),
			cancellationToken);

		html.Should().NotContain("<script>");
		html.Should().Contain("&lt;script&gt;");
		html.Should().Contain("&amp; Friends");
	}
}
