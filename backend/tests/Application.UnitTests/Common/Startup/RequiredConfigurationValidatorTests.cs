using Application.Common.Startup;
using AwesomeAssertions;

namespace Application.UnitTests.Common.Startup;

public class RequiredConfigurationValidatorTests
{
	[Test]
	public void FindMissing_ShouldReturnEmpty_WhenDevelopment_EvenWithNothingConfigured()
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: true,
			connectionString: null,
			keycloakClientSecret: null,
			authenticationAuthority: null,
			corsOrigins: null,
			smtpHost: null,
			smtpPort: null);

		missing.Should().BeEmpty();
	}

	[Test]
	public void FindMissing_ShouldReturnEmpty_WhenNotDevelopment_AndEverythingConfigured()
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().BeEmpty();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public void FindMissing_ShouldFlagConnectionString_WhenNotDevelopment_AndBlank(string? connectionString)
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: connectionString,
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("ConnectionStrings:afunto");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public void FindMissing_ShouldFlagKeycloakClientSecret_WhenNotDevelopment_AndBlank(string? keycloakClientSecret)
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: keycloakClientSecret,
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("Keycloak:ClientSecret");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public void FindMissing_ShouldFlagAuthenticationAuthority_WhenNotDevelopment_AndBlank(string? authenticationAuthority)
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: authenticationAuthority,
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("Authentication:Authority");
	}

	[Test]
	public void FindMissing_ShouldFlagCorsOrigins_WhenNotDevelopment_AndNull()
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: null,
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("Cors:Origins");
	}

	[Test]
	public void FindMissing_ShouldFlagCorsOrigins_WhenNotDevelopment_AndEmpty()
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: [],
			smtpHost: "smtp.example.com",
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("Cors:Origins");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public void FindMissing_ShouldFlagSmtpHost_WhenNotDevelopment_AndBlank(string? smtpHost)
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: smtpHost,
			smtpPort: "587");

		missing.Should().ContainSingle().Which.Should().Be("Smtp:Host");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	public void FindMissing_ShouldFlagSmtpPort_WhenNotDevelopment_AndBlank(string? smtpPort)
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: "Host=postgres;Database=afunto",
			keycloakClientSecret: "real-secret",
			authenticationAuthority: "https://login.example.com/realms/afunto",
			corsOrigins: ["https://afunto.example.com"],
			smtpHost: "smtp.example.com",
			smtpPort: smtpPort);

		missing.Should().ContainSingle().Which.Should().Be("Smtp:Port");
	}

	[Test]
	public void FindMissing_ShouldFlagAll_WhenNotDevelopment_AndNothingConfigured()
	{
		var missing = RequiredConfigurationValidator.FindMissing(
			isDevelopment: false,
			connectionString: null,
			keycloakClientSecret: null,
			authenticationAuthority: null,
			corsOrigins: null,
			smtpHost: null,
			smtpPort: null);

		missing.Should().BeEquivalentTo(
			"ConnectionStrings:afunto",
			"Keycloak:ClientSecret",
			"Authentication:Authority",
			"Cors:Origins",
			"Smtp:Host",
			"Smtp:Port");
	}
}
