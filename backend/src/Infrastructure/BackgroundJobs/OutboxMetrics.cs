using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace Infrastructure.BackgroundJobs;

internal sealed class OutboxMetrics
{
	// Registered with the OTel MeterProvider in Aspire/ServiceDefaults/Extensions.cs
	// (AddMeter) - keep both in sync if this ever changes.
	public const string MeterName = "Afunto.Outbox";

	private readonly Counter<long> _dispatchCounter;
	private readonly Gauge<long> _pendingGauge;

	public OutboxMetrics(IMeterFactory meterFactory)
	{
		var meter = meterFactory.Create(MeterName);

		_dispatchCounter = meter.CreateCounter<long>(
			"outbox.dispatch",
			unit: "{message}",
			description: "Outbox message dispatch attempts, tagged by outcome.");

		_pendingGauge = meter.CreateGauge<long>(
			"outbox.pending",
			unit: "{message}",
			description: "Outbox messages currently awaiting successful dispatch.");
	}

	public void RecordDispatched() => Record("succeeded");

	public void RecordFailed() => Record("failed");

	public void RecordDeadLettered() => Record("dead_lettered");

	public void RecordPending(long count) => _pendingGauge.Record(count);

	private void Record(string status) =>
		_dispatchCounter.Add(1, new KeyValuePair<string, object?>("status", status));
}
