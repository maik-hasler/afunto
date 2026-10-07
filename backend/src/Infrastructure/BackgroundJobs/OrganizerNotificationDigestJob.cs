using Application.Common.Email;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Domain.Organizations;
using Domain.Users;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

// Collapses everything EngagementOrganizerNotificationHelper queued (Application layer) into
// one email per organizer per tick, instead of one email per signup/withdrawal - the single
// biggest lever on outbound email volume, since a busy opportunity with several organizers
// used to multiply every volunteer action by the organizer count.
internal sealed class OrganizerNotificationDigestJob(
	IServiceScopeFactory scopeFactory,
	ILogger<OrganizerNotificationDigestJob> logger,
	IOptions<OrganizerNotificationDigestOptions> options)
	: IHostedService, IAsyncDisposable
{
	private readonly OrganizerNotificationDigestOptions _options = options.Value;

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
			// Mirrors OutboxProcessorJob/EngagementReminderJob: a failure here must not stop
			// the PeriodicTimer from ever being awaited again. Any item left un-digested still
			// has DigestSentOnUtc == null, so it is simply picked up again on the next poll.
			logger.LogError(ex, "Organizer notification digest tick failed; will retry on the next poll interval");
		}
	}

	private async Task TickAsync(CancellationToken ct)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
		var keycloakUserService = scope.ServiceProvider.GetRequiredService<IKeycloakUserService>();
		var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
		var emailTemplateRenderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
		var emailLinkBuilder = scope.ServiceProvider.GetRequiredService<IEmailLinkBuilder>();

		var digestedCount = await ProcessDigestBatchAsync(
			dbContext, keycloakUserService, emailService, emailTemplateRenderer, emailLinkBuilder, logger,
			_options.MaxBatchSize, ct);

		if (digestedCount > 0)
			logger.LogInformation("Sent {Count} organizer digest email(s)", digestedCount);
	}

	internal static async Task<int> ProcessDigestBatchAsync(
		ApplicationDbContext dbContext,
		IKeycloakUserService keycloakUserService,
		IEmailService emailService,
		IEmailTemplateRenderer emailTemplateRenderer,
		IEmailLinkBuilder emailLinkBuilder,
		ILogger logger,
		int batchSize,
		CancellationToken cancellationToken)
	{
		var items = await ClaimBatchAsync(dbContext, batchSize, cancellationToken);
		if (items.Count == 0)
			return 0;

		var itemsByOrganizer = items.GroupBy(i => i.OrganizerId).ToList();
		var organizerIds = itemsByOrganizer.Select(g => UserId.Create(g.Key).GetValueOrThrow()).ToList();

		var organizerUsersById = (await dbContext.GetOrCreateUsersAsync(organizerIds, cancellationToken))
			.ToDictionary(u => u.Id);
		var organizerProfilesById = await keycloakUserService.GetUserProfilesAsync(
			[.. organizerIds.Select(id => id.Value)], cancellationToken);

		var messages = new List<EmailMessage>(itemsByOrganizer.Count);
		var groupsByMessageIndex = new List<IGrouping<Guid, PendingOrganizerDigestItem>>(itemsByOrganizer.Count);

		foreach (var group in itemsByOrganizer)
		{
			if (!organizerProfilesById.TryGetValue(group.Key, out var profile))
			{
				logger.LogWarning(
					"Skipping organizer digest for organizer {OrganizerId}: no Keycloak profile found",
					group.Key);
				ReleaseClaim(group);
				continue;
			}

			var organizerId = UserId.Create(group.Key).GetValueOrThrow();
			var organizerLanguage = SupportedLanguages.Resolve(organizerUsersById[organizerId].PreferredLanguage);
			var organizerName = profile.FirstName ?? profile.Username;

			var orderedItems = group.OrderBy(i => i.OccurredOnUtc).ToList();
			var organizerUser = organizerUsersById[organizerId];

			// A digest can mix signups and withdrawals, and the footer has room for one
			// unsubscribe link - so only a single-kind digest gets one. The settings link
			// every email carries covers the mixed case.
			var kinds = orderedItems.Select(i => i.Kind).Distinct().ToList();
			var unsubscribeUrl = kinds.Count == 1
				? emailLinkBuilder.Unsubscribe(organizerId, organizerUser.UnsubscribeToken, kinds[0])
				: null;

			var draft = new EmailDraft(
				EmailTemplateKind.OrganizerDigest,
				organizerLanguage,
				organizerName,
				ManageUrl(orderedItems, emailLinkBuilder))
			{
				Count = orderedItems.Count,
				Lines = [.. orderedItems.Select(item => new EmailLine(
					item.Kind.ToString(),
					new Dictionary<string, string>
					{
						["VolunteerName"] = item.VolunteerName,
						["OpportunityTitle"] = item.OpportunityTitle,
					}))],
				UnsubscribeUrl = unsubscribeUrl,
			};

			messages.Add(new EmailMessage(profile.Email, emailTemplateRenderer.Render(draft), group.Key.ToString()));
			groupsByMessageIndex.Add(group);
		}

		if (messages.Count > 0)
		{
			var results = await emailService.SendBatchAsync(messages, cancellationToken);
			for (var i = 0; i < groupsByMessageIndex.Count; i++)
			{
				if (results[i])
					MarkDigested(groupsByMessageIndex[i]);
				else
					ReleaseClaim(groupsByMessageIndex[i]);
			}
		}

		await dbContext.SaveChangesAsync(cancellationToken);

		return messages.Count;
	}

	// The organization of the newest item - an organizer of several organizations lands
	// where the latest activity happened and can switch from there. Rows queued before the
	// column existed carry no organization and fall back to the home page.
	private static string ManageUrl(IReadOnlyList<PendingOrganizerDigestItem> orderedItems, IEmailLinkBuilder emailLinkBuilder)
	{
		var organizationId = orderedItems.LastOrDefault(i => i.OrganizationId is not null)?.OrganizationId;
		return organizationId is { } id
			? emailLinkBuilder.OrganizationEngagements(OrganizationId.Create(id).GetValueOrThrow())
			: emailLinkBuilder.Home();
	}

	private static void MarkDigested(IEnumerable<PendingOrganizerDigestItem> items)
	{
		var now = DateTime.UtcNow;
		foreach (var item in items)
			item.DigestSentOnUtc = now;
	}

	private static void ReleaseClaim(IEnumerable<PendingOrganizerDigestItem> items)
	{
		foreach (var item in items)
			item.ClaimedOnUtc = null;
	}

	internal static async Task<List<PendingOrganizerDigestItem>> ClaimBatchAsync(
		ApplicationDbContext dbContext,
		int batchSize,
		CancellationToken cancellationToken)
	{
		var strategy = dbContext.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async _ =>
		{
			// FOR UPDATE SKIP LOCKED, same reasoning as OutboxProcessorJob.ClaimBatchAsync:
			// without it, two replicas ticking concurrently would both claim and digest the
			// same rows, sending the same organizer two emails for the same events.
			await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

			var now = DateTime.UtcNow;
			// A generous staleness window relative to the multi-hour poll interval: this only
			// matters if a previous tick claimed a batch and then crashed before persisting
			// any DigestSentOnUtc/ClaimedOnUtc release, so it errs toward not re-claiming a
			// batch that is still legitimately being processed.
			var staleCutoff = now.AddHours(-1);

			var items = await dbContext.Set<PendingOrganizerDigestItem>()
				.FromSqlInterpolated($@"
					SELECT id, organizer_id, organization_id, opportunity_title, volunteer_name, kind, occurred_on_utc, claimed_on_utc, digest_sent_on_utc
					FROM pending_organizer_digest_item
					WHERE digest_sent_on_utc IS NULL
						AND (claimed_on_utc IS NULL OR claimed_on_utc <= {staleCutoff})
					ORDER BY occurred_on_utc
					LIMIT {batchSize}
					FOR UPDATE SKIP LOCKED")
				.ToListAsync(cancellationToken);

			foreach (var item in items)
				item.ClaimedOnUtc = now;

			if (items.Count > 0)
				await dbContext.SaveChangesAsync(cancellationToken);

			await transaction.CommitAsync(cancellationToken);

			return items;
		}, cancellationToken);
	}
}
