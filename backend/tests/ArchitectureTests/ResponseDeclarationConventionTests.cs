using Api.Common.Authentication;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ArchitectureTests;

/// <summary>
/// The committed OpenAPI document is the only input both generated clients are built from, so a
/// response an endpoint can really return but never declares is invisible to every caller: the
/// typed integration client cannot assert on it and the frontend client has no branch for it.
/// #2158 was exactly that (UndoCheckInEngagement returned 409 undeclared) and no test caught it -
/// a human read the generated code. These conventions read the route metadata instead, so they
/// reach all 101 operations rather than only the ones whose body the typed client can check, and
/// they run in the no-Docker lane where IntegrationTests cannot.
/// </summary>
public sealed class ResponseDeclarationConventionTests
{
	/// <summary>
	/// Policies that an authenticated realm user can still fail, so the authorization middleware
	/// really does produce a 403 for them. Per keycloak/realms/afunto-realm.json,
	/// "default-roles-afunto" is composite over "user", so every registered user carries
	/// the "user" role and AfuntoDefaultUserPolicy can only ever answer 401 (no token) or
	/// pass - which is why it is deliberately absent here. "organisator" is not in the default
	/// set and "admin" is granted by hand, so both can deny a signed-in caller.
	/// </summary>
	private static readonly string[] RoleRestrictedPolicies =
	[
		AuthorizationPolicies.AfuntoAdminPolicy,
		AuthorizationPolicies.AfuntoOrganisatorPolicy
	];

	/// <summary>
	/// Statuses whose contract is "no body", so declaring one without a response type is correct
	/// rather than an omission.
	/// </summary>
	private static readonly int[] BodilessSuccessStatusCodes =
	[
		StatusCodes.Status204NoContent,
		StatusCodes.Status205ResetContent,
		StatusCodes.Status304NotModified
	];

	[Test]
	public void AllEndpoints_ShouldDeclare_ASuccessResponse()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var endpointsWithoutSuccessResponse = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => !SuccessResponses(e).Any())
			.Select(Describe)
			.ToList();

		endpointsWithoutSuccessResponse.Should().BeEmpty(
			"every endpoint must declare the 2xx it returns via .Produces<T>() / .Produces(status) - " +
			"an operation with no success response generates a client method that silently returns " +
			"nothing, so no caller can be written against what the endpoint actually sends back - offenders: {0}",
			string.Join(", ", endpointsWithoutSuccessResponse));
	}

	[Test]
	public void SuccessResponsesWithoutABodyType_ShouldDeclareAContentType_OrABodilessStatus()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var unexplainedEmptyBodies = EndpointTestHelper.GetAllRouteEndpoints(app)
			.SelectMany(e => SuccessResponses(e)
				.Where(m => !HasBodyType(m))
				.Where(m => !BodilessSuccessStatusCodes.Contains(m.StatusCode))
				.Where(m => !m.ContentTypes.Any())
				.Select(m => $"{Describe(e)} declares {m.StatusCode} with neither a response type nor a content type"))
			.ToList();

		unexplainedEmptyBodies.Should().BeEmpty(
			"a 2xx that carries a body must say what that body is - either .Produces<T>() for JSON " +
			"or .Produces(status, contentType: ...) for the bytes the endpoint streams (the map " +
			"tile, the meta HTML, the sitemap XML) - because a bare .Produces(200) tells the client " +
			"generator only that the call succeeded, which is indistinguishable from a 204 and is " +
			"how a response body goes missing from the contract without anyone noticing - offenders: {0}",
			string.Join(", ", unexplainedEmptyBodies));
	}

	[Test]
	public void EndpointsThatRequireAuthorization_ShouldDeclare_Unauthorized()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var endpointsWithoutUnauthorized = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => !e.Metadata.OfType<IAllowAnonymous>().Any())
			.Where(e => e.Metadata.OfType<IAuthorizeData>().Any())
			.Where(e => !DeclaresStatus(e, StatusCodes.Status401Unauthorized))
			.Select(Describe)
			.ToList();

		endpointsWithoutUnauthorized.Should().BeEmpty(
			"an endpoint behind RequireAuthorization(...) answers 401 for a missing or expired token " +
			"before the handler ever runs, so the document must declare it - the frontend's refresh " +
			"and re-login path keys off that status - offenders: {0}",
			string.Join(", ", endpointsWithoutUnauthorized));
	}

	[Test]
	public void EndpointsBehindARoleRestrictedPolicy_ShouldDeclare_Forbidden()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var endpointsWithoutForbidden = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => e.Metadata.OfType<IAuthorizeData>()
				.Any(a => a.Policy is not null && RoleRestrictedPolicies.Contains(a.Policy)))
			.Where(e => !DeclaresStatus(e, StatusCodes.Status403Forbidden))
			.Select(Describe)
			.ToList();

		endpointsWithoutForbidden.Should().BeEmpty(
			$"a signed-in user without the required role fails {nameof(AuthorizationPolicies.AfuntoAdminPolicy)} " +
			$"or {nameof(AuthorizationPolicies.AfuntoOrganisatorPolicy)} and gets a 403 from the " +
			"authorization middleware; the organization-scoped handlers behind those policies raise a " +
			"second 403 of their own through OwnershipGuard, so an endpoint that omits it is documenting " +
			"a denial path it demonstrably has - offenders: {0}",
			string.Join(", ", endpointsWithoutForbidden));
	}

	[Test]
	public void EndpointsThatAcceptARequestBody_ShouldDeclare_BadRequest()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var endpointsWithoutBadRequest = EndpointTestHelper.GetAllRouteEndpoints(app)
			.Where(e => e.Metadata.GetMetadata<IAcceptsMetadata>() is not null)
			.Where(e => !DeclaresStatus(e, StatusCodes.Status400BadRequest))
			.Select(Describe)
			.ToList();

		endpointsWithoutBadRequest.Should().BeEmpty(
			"minimal API parameter binding rejects a malformed or unreadable body with a 400 before the " +
			"handler is entered, so every endpoint that accepts one can produce it whatever the handler " +
			"does - and several handlers here return Results.Problem(statusCode: 400) on top of that - offenders: {0}",
			string.Join(", ", endpointsWithoutBadRequest));
	}

	[Test]
	public void ErrorResponses_ShouldBeDeclaredAs_ProblemDetails()
	{
		var app = EndpointTestHelper.BuildMinimalAppWithAllEndpoints();

		var errorsThatAreNotProblemDetails = EndpointTestHelper.GetAllRouteEndpoints(app)
			.SelectMany(e => e.Metadata.OfType<IProducesResponseTypeMetadata>()
				.Where(m => m.StatusCode >= StatusCodes.Status400BadRequest)
				.Where(m => m.Type is null || !typeof(ProblemDetails).IsAssignableFrom(m.Type))
				.Select(m => $"{Describe(e)} declares {m.StatusCode} as '{m.Type?.Name ?? "no type"}'"))
			.ToList();

		errorsThatAreNotProblemDetails.Should().BeEmpty(
			"the API returns every failure as ProblemDetails, so error statuses must be declared with " +
			".ProducesProblem(...) rather than .Produces(status) - the bare overload types the error as " +
			"the success body (or as nothing), which is how a generated client ends up unable to read " +
			"the message it was handed - offenders: {0}",
			string.Join(", ", errorsThatAreNotProblemDetails));
	}

	private static IEnumerable<IProducesResponseTypeMetadata> SuccessResponses(RouteEndpoint endpoint) =>
		endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
			.Where(m => m.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices);

	private static bool DeclaresStatus(RouteEndpoint endpoint, int statusCode) =>
		endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Any(m => m.StatusCode == statusCode);

	// .Produces(status) records typeof(void) rather than null, so both spellings mean "no body".
	private static bool HasBodyType(IProducesResponseTypeMetadata metadata) =>
		metadata.Type is not null && metadata.Type != typeof(void);

	private static string Describe(RouteEndpoint endpoint)
	{
		var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
		return $"{string.Join("/", methods)} {endpoint.RoutePattern.RawText}";
	}
}
