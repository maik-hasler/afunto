namespace Application.Common.Startup;

public static class RequiredConfigurationValidator
{
	public static IReadOnlyList<string> FindMissing(
		bool isDevelopment,
		string? connectionString,
		string? keycloakClientSecret,
		string? authenticationAuthority,
		string[]? corsOrigins,
		string? smtpHost,
		string? smtpPort)
	{
		if (isDevelopment)
			return [];

		var missing = new List<string>();

		if (string.IsNullOrWhiteSpace(connectionString))
			missing.Add("ConnectionStrings:afunto");

		if (string.IsNullOrWhiteSpace(keycloakClientSecret))
			missing.Add("Keycloak:ClientSecret");

		if (string.IsNullOrWhiteSpace(authenticationAuthority))
			missing.Add("Authentication:Authority");

		if (corsOrigins is not { Length: > 0 })
			missing.Add("Cors:Origins");

		if (string.IsNullOrWhiteSpace(smtpHost))
			missing.Add("Smtp:Host");

		if (string.IsNullOrWhiteSpace(smtpPort))
			missing.Add("Smtp:Port");

		return missing;
	}
}
