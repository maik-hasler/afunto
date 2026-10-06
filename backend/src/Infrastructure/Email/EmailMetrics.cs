using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Infrastructure.Email;

internal sealed class EmailMetrics
{
	// Registered with the OTel MeterProvider in Aspire/ServiceDefaults/Extensions.cs
	// (AddMeter) - keep both in sync if this ever changes.
	public const string MeterName = "Afunto.Email";

	private readonly Counter<long> _sendCounter;

	public EmailMetrics(IMeterFactory meterFactory)
	{
		_sendCounter = meterFactory.Create(MeterName).CreateCounter<long>(
			"email.send",
			unit: "{email}",
			description: "Outbound email send attempts, tagged by outcome.");
	}

	public void RecordSucceeded() => Record("succeeded");

	public void RecordFailed() => Record("failed");

	private void Record(string status) =>
		_sendCounter.Add(1, new KeyValuePair<string, object?>("status", status));
}
