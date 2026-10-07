using Domain.Engagements;
using Domain.VolunteerOpportunities;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

internal sealed class EngagementReminderJob(
	IServiceScopeFactory scopeFactory,
	ILogger<EngagementReminderJob> logger,
	IOptions<EngagementReminderOptions> options)
	: IHostedService, IAsyncDisposable
{
	private readonly EngagementReminderOptions _options = options.Value;

	private Task _executeTask = Task.CompletedTask;
	private CancellationTokenSource? _cts;
	private PeriodicTimer? _timer;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_timer = new PeriodicTimer(TimeSpan.FromHours(_options.PollIntervalHours));
		_executeTask = RunLoopAsync(_cts.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_cts is not null)
			await _cts.CancelAsync();

		try
		{
			await _executeTask.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
	}

	public ValueTask DisposeAsync()
	{
		_timer?.Dispose();
		_cts?.Dispose();
		return ValueTask.CompletedTask;
	}

	private async Task RunLoopAsync(CancellationToken ct)
	{
		if (_timer is null) return;

		if (!ct.IsCancellationRequested)
			await RunTickWithErrorHandlingAsync(ct).ConfigureAwait(false);

		while (!ct.IsCancellationRequested && await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
		{
			await RunTickWithErrorHandlingAsync(ct).ConfigureAwait(false);
		}
	}

	private async Task RunTickWithErrorHandlingAsync(CancellationToken ct)
	{
		try
		{
			await TickAsync(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Mirrors OutboxProcessorJob's tick-level catch: a failure here (e.g. a
			// transient DB error) must not stop the PeriodicTimer from ever being
			// awaited again. Any due engagement still has ReminderSentAt == null,
			// so it is simply picked up again on the next tick.
			logger.LogError(ex, "Engagement reminder tick failed; will retry on the next poll interval");
		}
	}

	private async Task TickAsync(CancellationToken ct)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

		var queued = await ClaimAndQueueRemindersAsync(dbContext, DateTimeOffset.UtcNow, _options.MaxBatchSize, ct);

		if (queued > 0)
			logger.LogInformation("Queued {Count} reminder(s) for outbox dispatch", queued);
	}

	internal static async Task<int> ClaimAndQueueRemindersAsync(
		ApplicationDbContext dbContext,
		DateTimeOffset now,
		int maxBatchSize,
		CancellationToken cancellationToken = default)
	{
		var windowEnd = now + Engagement.ReminderLeadTime;

		// A shift's start can pass while its engagement is still unreminded - a long
		// outage, a backlog bigger than maxBatchSize, or (before this fix) a slot that
		// never fell inside the old fixed [-23h, -25h] scan window. Mark those claimed
		// without sending, so they stop lingering as pending reminders instead of
		// getting a reminder for a shift that has already begun.
		await dbContext.Set<Engagement>()
			.Where(e =>
				e.Status == EngagementStatus.Confirmed &&
				e.TimeSlotId != null &&
				e.ReminderSentAt == null &&
				dbContext.Set<TimeSlot>().Any(ts => ts.Id == e.TimeSlotId && ts.StartDateTime <= now))
			.ExecuteUpdateAsync(
				s => s
					.SetProperty(e => e.ReminderSentAt, now)
					.SetProperty(e => e.ModifiedOn, now),
				cancellationToken);

		var strategy = dbContext.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync<int>(async _ =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

			var candidates = await dbContext.Set<Engagement>()
				.Where(e =>
					e.Status == EngagementStatus.Confirmed &&
					e.TimeSlotId != null &&
					e.ReminderSentAt == null)
				.Join(
					dbContext.Set<TimeSlot>(),
					e => e.TimeSlotId,
					ts => ts.Id,
					(e, ts) => new { e.Id, e.VolunteerId, e.OpportunityId, e.TimeSlotId, ts.StartDateTime })
				.Where(x => x.StartDateTime > now && x.StartDateTime <= windowEnd)
				.OrderBy(x => x.StartDateTime)
				.Take(maxBatchSize)
				.ToListAsync(cancellationToken);

			if (candidates.Count == 0)
			{
				await transaction.CommitAsync(cancellationToken);
				return 0;
			}

			var occurredOnUtc = now.UtcDateTime;
			var claimedMessages = new List<OutboxMessage>(candidates.Count);

			foreach (var candidate in candidates)
			{
				var rowsAffected = await dbContext.Set<Engagement>()
					.Where(e => e.Id == candidate.Id && e.ReminderSentAt == null)
					.ExecuteUpdateAsync(
						s => s
							.SetProperty(e => e.ReminderSentAt, now)
							.SetProperty(e => e.ModifiedOn, now),
						cancellationToken);

				if (rowsAffected == 0)
					continue;

				var domainEvent = new EngagementReminderDueDomainEvent(
					candidate.Id, candidate.VolunteerId!.Value, candidate.OpportunityId, candidate.TimeSlotId!.Value);
				claimedMessages.Add(OutboxMessage.FromDomainEvent(domainEvent, occurredOnUtc));
			}

			if (claimedMessages.Count == 0)
			{
				await transaction.CommitAsync(cancellationToken);
				return 0;
			}

			// ExecuteUpdateAsync above bypasses the ChangeTracker, so
			// ConvertDomainEventsToOutboxMessagesInterceptor never sees these events - the
			// outbox rows are built directly here instead, then written in a single batched
			// SaveChangesAsync for however many this tick actually won.
			dbContext.Set<OutboxMessage>().AddRange(claimedMessages);
			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);

			return claimedMessages.Count;
		}, cancellationToken);
	}
}
