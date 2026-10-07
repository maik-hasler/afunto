namespace Application.Common.Email;

public sealed record EmailMessage(string To, RenderedEmail Content, string CorrelationId);

public interface IEmailService
{
	Task SendAsync(
		EmailMessage message,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<bool>> SendBatchAsync(
		IReadOnlyList<EmailMessage> messages,
		CancellationToken cancellationToken = default);
}
