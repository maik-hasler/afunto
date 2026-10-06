namespace Infrastructure.Email;

internal sealed class SmtpOptions
{
	public string Host { get; init; } = "localhost";
	public int Port { get; init; } = 1025;
	public string FromAddress { get; init; } = "noreply@afunto.local";
	public string FromName { get; init; } = "Afunto";
	public string? Username { get; init; }
	public string? Password { get; init; }
	public bool EnableSsl { get; init; }

	// Hetzner's webhosting SMTP accounts hard-cap outbound mail at 500/hour and block the
	// account if it's exceeded - this default leaves headroom below that limit rather than
	// matching it exactly. Configurable per-deployment via "Smtp:MaxEmailsPerHour" since the
	// real limit depends on the hosting contract in use.
	public int MaxEmailsPerHour { get; init; } = 400;
}
