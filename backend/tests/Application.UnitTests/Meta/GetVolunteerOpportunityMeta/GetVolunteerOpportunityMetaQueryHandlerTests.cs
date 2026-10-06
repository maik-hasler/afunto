using Application.Meta.GetVolunteerOpportunityMeta.v1;
using Application.VolunteerOpportunities;
using Application.VolunteerOpportunities.GetVolunteerOpportunityDetails.v1;
using AwesomeAssertions;
using NSubstitute;

namespace Application.UnitTests.Meta.GetVolunteerOpportunityMeta;

public class GetVolunteerOpportunityMetaQueryHandlerTests
{
	private readonly IVolunteerOpportunityReadRepository _readRepository =
		Substitute.For<IVolunteerOpportunityReadRepository>();
	private readonly GetVolunteerOpportunityMetaQueryHandler _sut;

	public GetVolunteerOpportunityMetaQueryHandlerTests()
	{
		_sut = new GetVolunteerOpportunityMetaQueryHandler(_readRepository);
	}

	private static VolunteerOpportunityDetails CreateDetails(
		Guid? id = null,
		string title = "Strandreinigung",
		string? description = "Wir sammeln gemeinsam Müll am Strand.",
		string? bannerImageUrl = null) =>
		new(
			id ?? Guid.NewGuid(),
			title,
			null,
			description,
			null,
			Guid.NewGuid(),
			"Küstenschutz e.V.",
			"Strandweg",
			"1",
			"12345",
			"Musterstadt",
			null,
			null,
			false,
			"OneTime",
			"ScheduledSlots",
			"None",
			null,
			[],
			[],
			DateTimeOffset.UtcNow,
			null,
			0,
			"Published",
			bannerImageUrl,
			[]);

	[Test]
	public async Task Handle_ShouldReturnNull_WhenOpportunityNotFound(CancellationToken cancellationToken)
	{
		_readRepository
			.GetDetailsAsync(Arg.Any<Guid>(), null, Arg.Any<CancellationToken>())
			.Returns((VolunteerOpportunityDetails?)null);

		var result = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(Guid.NewGuid(), "https://afunto.example"),
			cancellationToken);

		result.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldIncludeTitleDescriptionAndCanonicalUrl_WhenOpportunityFound(
		CancellationToken cancellationToken)
	{
		var opportunityId = Guid.NewGuid();
		var details = CreateDetails(id: opportunityId);
		_readRepository
			.GetDetailsAsync(opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(details);

		var html = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(opportunityId, "https://afunto.example/"),
			cancellationToken);

		html.Should().NotBeNull();
		html.Should().Contain("Strandreinigung - Afunto");
		html.Should().Contain("Wir sammeln gemeinsam Müll am Strand.");
		html.Should().Contain($"https://afunto.example/volunteer-opportunities/{opportunityId}");
	}

	[Test]
	public async Task Handle_ShouldFallBackToSiteOgImage_WhenOpportunityHasNoBanner(
		CancellationToken cancellationToken)
	{
		var opportunityId = Guid.NewGuid();
		_readRepository
			.GetDetailsAsync(opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(CreateDetails(bannerImageUrl: null));

		var html = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(opportunityId, "https://afunto.example"),
			cancellationToken);

		html.Should().Contain("https://afunto.example/og-image.png");
	}

	[Test]
	public async Task Handle_ShouldUseOpportunityBanner_WhenSet(CancellationToken cancellationToken)
	{
		var opportunityId = Guid.NewGuid();
		_readRepository
			.GetDetailsAsync(opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(CreateDetails(bannerImageUrl: "https://storage.example/banners/abc.png"));

		var html = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(opportunityId, "https://afunto.example"),
			cancellationToken);

		html.Should().Contain("https://storage.example/banners/abc.png");
		html.Should().NotContain("og-image.png");
	}

	[Test]
	public async Task Handle_ShouldNotSplitSurrogatePair_WhenTruncatingLongDescription(
		CancellationToken cancellationToken)
	{
		var longDescription = new string('a', 199) + "\U0001F600" + new string('b', 50);
		var opportunityId = Guid.NewGuid();
		_readRepository
			.GetDetailsAsync(opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(CreateDetails(description: longDescription));

		var html = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(opportunityId, "https://afunto.example"),
			cancellationToken);

		html.Should().Contain(new string('a', 199) + "...");
		html.Should().NotContain("\U0001F600");
		html.Should().NotContain("\uD83D").And.NotContain("\uDE00");
	}

	[Test]
	public async Task Handle_ShouldHtmlEncodeOpportunityTitle_ToPreventMarkupInjection(
		CancellationToken cancellationToken)
	{
		var opportunityId = Guid.NewGuid();
		_readRepository
			.GetDetailsAsync(opportunityId, null, Arg.Any<CancellationToken>())
			.Returns(CreateDetails(title: "<script>alert(1)</script> & Friends"));

		var html = await _sut.Handle(
			new GetVolunteerOpportunityMetaQuery(opportunityId, "https://afunto.example"),
			cancellationToken);

		html.Should().NotContain("<script>");
		html.Should().Contain("&lt;script&gt;");
		html.Should().Contain("&amp; Friends");
	}
}
