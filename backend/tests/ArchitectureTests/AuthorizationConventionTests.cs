using Api.Common.Authentication;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;

namespace ArchitectureTests;

public sealed class AuthorizationConventionTests
{
	[Test]
	public void AllEndpoints_ShouldDeclareAnExplicitAuthorizationDecision()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var endpointsWithoutAuthorizationMetadata = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => !e.Metadata.OfType<IAllowAnonymous>().Any()
				&& !e.Metadata.OfType<IAuthorizeData>().Any())
			.Select(e => e.RoutePattern.RawText)
			.ToList();

		endpointsWithoutAuthorizationMetadata.Should().BeEmpty(
			"every endpoint must call RequireAuthorization(...) or AllowAnonymous() explicitly - " +
			"omitting both leaves it anonymously reachable by accident");
	}

	[Test]
	public void AdminRoutes_ShouldRequireTheAdminPolicy()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var adminRoutesWithoutAdminPolicy = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => e.RoutePattern.RawText?.Contains("/admin/") == true)
			.Where(e => e.Metadata.OfType<IAuthorizeData>()
				.All(a => a.Policy != AuthorizationPolicies.AfuntoAdminPolicy))
			.Select(e => e.RoutePattern.RawText)
			.ToList();

		adminRoutesWithoutAdminPolicy.Should().BeEmpty(
			$"every /admin/ route must require {nameof(AuthorizationPolicies.AfuntoAdminPolicy)} - " +
			"any other policy (or none at all) would let a non-admin user reach it");
	}
}
