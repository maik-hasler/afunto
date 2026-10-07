using Application.Common.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructure.Email;

internal sealed class SmtpEmailService(
	IOptions<SmtpOptions> options,
	ILogger<SmtpEmailService> logger,
	EmailMetrics metrics,
	EmailRateLimiter rateLimiter)
	: IEmailService
{
	private readonly SmtpOptions _options = options.Value;

	public async Task SendAsync(
		EmailMessage message,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var client = new SmtpClient();
			await ConnectAsync(client, cancellationToken);

			using var mimeMessage = CreateMimeMessage(message);

			await rateLimiter.WaitForPermitAsync(cancellationToken);
			await client.SendAsync(mimeMessage, cancellationToken);
			await client.DisconnectAsync(true, cancellationToken);

			metrics.RecordSucceeded();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send email (correlationId: {CorrelationId})", message.CorrelationId);
			metrics.RecordFailed();
			throw;
		}
	}

	public async Task<IReadOnlyList<bool>> SendBatchAsync(
		IReadOnlyList<EmailMessage> messages,
		CancellationToken cancellationToken = default)
	{
		var results = new bool[messages.Count];
		if (messages.Count == 0)
			return results;

		using var client = new SmtpClient();

		try
		{
			await ConnectAsync(client, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to establish SMTP connection for batch of {Count} emails", messages.Count);
			for (var i = 0; i < messages.Count; i++)
				metrics.RecordFailed();

			return results;
		}

		for (var i = 0; i < messages.Count; i++)
		{
			var message = messages[i];
			try
			{
				using var mimeMessage = CreateMimeMessage(message);

				await rateLimiter.WaitForPermitAsync(cancellationToken);
				await client.SendAsync(mimeMessage, cancellationToken);

				metrics.RecordSucceeded();
				results[i] = true;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to send email (correlationId: {CorrelationId})", message.CorrelationId);
				metrics.RecordFailed();
			}
		}

		try
		{
			await client.DisconnectAsync(true, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to cleanly disconnect SMTP client after sending a batch of {Count} emails", messages.Count);
		}

		return results;
	}

	private async Task ConnectAsync(SmtpClient client, CancellationToken cancellationToken)
	{
		var secureSocketOptions = _options.EnableSsl
			? SecureSocketOptions.StartTls
			: SecureSocketOptions.None;
		await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, cancellationToken);

		if (!string.IsNullOrEmpty(_options.Username))
			await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
	}

	// Deliberately no X-Priority/Importance/Priority header: their absence means "normal"
	// (RFC 2156), and nothing Afunto sends is urgent (#2402).
	internal MimeMessage CreateMimeMessage(EmailMessage message)
	{
		var mimeMessage = new MimeMessage();
		mimeMessage.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
		mimeMessage.To.Add(MailboxAddress.Parse(message.To));
		mimeMessage.Subject = message.Content.Subject;

		// RFC 3834: marks the message as machine-generated, which suppresses out-of-office
		// auto-replies and lets clients file it as a notification rather than personal mail.
		mimeMessage.Headers.Add("Auto-Submitted", "auto-generated");

		// RFC 2369: lets mail clients offer their own "unsubscribe" control. It points at
		// the same confirm page as the footer link, so nothing unsubscribes without a click.
		if (message.Content.UnsubscribeUrl is { } unsubscribeUrl)
			mimeMessage.Headers.Add(HeaderId.ListUnsubscribe, $"<{unsubscribeUrl}>");

		mimeMessage.Body = new BodyBuilder
		{
			TextBody = message.Content.TextBody,
			HtmlBody = message.Content.HtmlBody,
		}.ToMessageBody();

		return mimeMessage;
	}
}
