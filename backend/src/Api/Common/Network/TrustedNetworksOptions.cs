namespace Api.Common.Network;

// Which immediate-connection sources are trusted to set X-Forwarded-For (used by
// ForwardedHeadersMiddleware in Program.cs). The default covers loopback and every
// RFC1918 private range - safe in every environment this backend actually runs in,
// since a genuine off-host attacker can never make a real network connection whose
// source address falls in these ranges (see afunto#1332):
//   - Behind a reverse proxy: the proxy is the only thing that can reach the
//     backend container at all (it publishes no host port), and it always connects
//     from a container network in a private range.
//   - Local dev / IntegrationTests / VisualTests (Aspire AppHost): the backend is
//     only ever reached over loopback.
// Anything arriving from outside these ranges is, by construction, not the
// runtime's own reverse proxy - its X-Forwarded-For is ignored entirely rather
// than trusted, closing the anonymous-rate-limit bypass this header enabled.
internal sealed class TrustedNetworksOptions
{
	public string[] Cidrs { get; init; } =
	[
		"127.0.0.0/8",
		"::1/128",
		"10.0.0.0/8",
		"172.16.0.0/12",
		"192.168.0.0/16",
	];
}
