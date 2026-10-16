using System.Security.Claims;
using Api.Common.Authentication;
using AwesomeAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IntegrationTests;

/// <summary>
/// The `iat` comparison on its own - no stack needed. A token from the second
/// of the change counts as fresh, so the renewal the client fetches right
/// after the 401 is accepted.
/// </summary>
public class PermissionsChangedTokenValidatorTests
{
	private static readonly DateTimeOffset ChangedOn =
		new(2026, 10, 15, 14, 0, 0, 700, TimeSpan.Zero);

	[Test]
	[Arguments(-1, true)]
	[Arguments(0, false)]
	[Arguments(1, false)]
	public void IsIssuedBefore_ShouldOnlyRejectTokensFromAnEarlierSecond(
		int issuedSecondsAfterChange, bool expected)
	{
		// Arrange
		var token = CreateToken(ChangedOn.AddSeconds(issuedSecondsAfterChange).UtcDateTime);

		// Act
		var issuedBefore = PermissionsChangedTokenValidator.IsIssuedBefore(token, ChangedOn);

		// Assert
		issuedBefore.Should().Be(expected);
	}

	[Test]
	public void IsIssuedBefore_ShouldRejectATokenWithoutIssuedAt()
	{
		// Arrange
		var token = CreateToken(issuedAt: null);

		// Act
		var issuedBefore = PermissionsChangedTokenValidator.IsIssuedBefore(token, ChangedOn);

		// Assert
		issuedBefore.Should().BeTrue();
	}

	private static JsonWebToken CreateToken(DateTime? issuedAt)
	{
		var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };

		return new JsonWebToken(handler.CreateToken(new SecurityTokenDescriptor
		{
			Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
			IssuedAt = issuedAt,
		}));
	}
}
