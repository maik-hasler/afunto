using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Application.Common.Email;
using AwesomeAssertions;
using Infrastructure.Email;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IntegrationTests;

[NotInParallel(nameof(SmtpEmailServiceTests))]
public class SmtpEmailServiceTests
{
	[Test]
	public async Task SendAsync_ServerAcceptsMail_RecordsSucceededMetricAndDoesNotThrow()
	{
		await using var server = await FakeSmtpServer.StartAsync();
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		var sut = CreateService(server.Port, metrics);
		var act = () => sut.SendAsync(Message("volunteer@example.com", "Test subject", "test-correlation-id"));

		await act.Should().NotThrowAsync();

		recorded.Should().ContainSingle(m => m.Status == "succeeded" && m.Value == 1);
	}

	[Test]
	public async Task SendAsync_ServerUnreachable_RecordsFailedMetricAndRethrows()
	{
		var unusedPort = GetUnusedLoopbackPort();
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		var sut = CreateService(unusedPort, metrics);
		var act = () => sut.SendAsync(Message("volunteer@example.com", "Test subject", "test-correlation-id"));

		await act.Should().ThrowAsync<Exception>(
			"a swallowed failure here would let the outbox believe a notification was delivered when it never left the process");

		recorded.Should().ContainSingle(m => m.Status == "failed" && m.Value == 1);
	}

	[Test]
	public async Task SendAsync_ServerUnreachable_LogsCorrelationIdWithoutRecipientOrSubject()
	{
		var unusedPort = GetUnusedLoopbackPort();
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var logger = new FakeLogger<SmtpEmailService>();

		var sut = CreateService(unusedPort, metrics, logger);
		var act = () => sut.SendAsync(Message("volunteer@example.com", "New sign-up: Vera joined \"Beach Cleanup\"", "engagement-correlation-id"));
		await act.Should().ThrowAsync<Exception>();

		var record = logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error).Subject;
		record.Message.Should().Contain("engagement-correlation-id");
		record.Message.Should().NotContain("volunteer@example.com");
		record.Message.Should().NotContain("Vera");
		record.Message.Should().NotContain("Beach Cleanup");
	}

	[Test]
	public async Task SendBatchAsync_ServerAcceptsAllMessages_SendsEveryMessageOverOneConnection()
	{
		await using var server = await FakeSmtpServer.StartAsync();
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		var sut = CreateService(server.Port, metrics);
		var messages = new[]
		{
			Message("vera@example.com", "Reminder 1", "corr-1"),
			Message("olaf@example.com", "Reminder 2", "corr-2"),
			Message("admin@example.com", "Reminder 3", "corr-3"),
		};

		var results = await sut.SendBatchAsync(messages);

		results.Should().Equal(true, true, true);
		recorded.Count(m => m.Status == "succeeded").Should().Be(3);
		recorded.Should().NotContain(m => m.Status == "failed");
	}

	[Test]
	public async Task SendBatchAsync_ServerRejectsOneRecipient_ReportsThatMessageAsFailedAndKeepsSendingTheRest()
	{
		await using var server = await FakeSmtpServer.StartAsync(rejectRecipient: "olaf@example.com");
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		var sut = CreateService(server.Port, metrics);
		var messages = new[]
		{
			Message("vera@example.com", "Reminder 1", "corr-1"),
			Message("olaf@example.com", "Reminder 2", "corr-2"),
			Message("admin@example.com", "Reminder 3", "corr-3"),
		};

		var results = await sut.SendBatchAsync(messages);

		results.Should().Equal(true, false, true);
		recorded.Count(m => m.Status == "succeeded").Should().Be(2);
		recorded.Count(m => m.Status == "failed").Should().Be(1);
	}

	[Test]
	public async Task SendBatchAsync_ServerRejectsOneRecipient_LogsCorrelationIdWithoutRecipientOrSubject()
	{
		await using var server = await FakeSmtpServer.StartAsync(rejectRecipient: "olaf@example.com");
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var logger = new FakeLogger<SmtpEmailService>();

		var sut = CreateService(server.Port, metrics, logger);
		var messages = new[]
		{
			Message("vera@example.com", "Reminder 1", "corr-1"),
			Message("olaf@example.com", "New sign-up: Vera joined \"Beach Cleanup\"", "corr-2-rejected"),
			Message("admin@example.com", "Reminder 3", "corr-3"),
		};

		await sut.SendBatchAsync(messages);

		var record = logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error).Subject;
		record.Message.Should().Contain("corr-2-rejected");
		record.Message.Should().NotContain("olaf@example.com");
		record.Message.Should().NotContain("Vera");
		record.Message.Should().NotContain("Beach Cleanup");
	}

	[Test]
	public async Task SendBatchAsync_ServerUnreachable_AllMessagesFailWithoutThrowing()
	{
		var unusedPort = GetUnusedLoopbackPort();
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		var sut = CreateService(unusedPort, metrics);
		var messages = new[]
		{
			Message("vera@example.com", "Reminder 1", "corr-1"),
			Message("olaf@example.com", "Reminder 2", "corr-2"),
		};

		var results = await sut.SendBatchAsync(messages);

		results.Should().Equal(false, false);
		recorded.Count(m => m.Status == "failed").Should().Be(2);
	}

	[Test]
	public async Task SendBatchAsync_EmptyMessageList_ReturnsEmptyResultsWithoutConnecting()
	{
		using var meterFactory = new TestMeterFactory();
		var metrics = new EmailMetrics(meterFactory);
		var recorded = RecordEmailSendMeasurements(meterFactory);

		// Deliberately no FakeSmtpServer started: an empty batch must not attempt
		// a connection at all, so an unused port never being listened on is fine.
		var sut = CreateService(GetUnusedLoopbackPort(), metrics);

		var results = await sut.SendBatchAsync([]);

		results.Should().BeEmpty();
		recorded.Should().BeEmpty();
	}

	[Test]
	public void CreateMimeMessage_CarriesPlainTextAndHtmlAlternatives()
	{
		using var meterFactory = new TestMeterFactory();
		var sut = CreateService(GetUnusedLoopbackPort(), new EmailMetrics(meterFactory));

		using var mime = sut.CreateMimeMessage(Message("vera@example.com", "Subject", "corr"));

		mime.TextBody.Should().Be("Text body");
		mime.HtmlBody.Should().Be("<p>HTML body</p>");
	}

	[Test]
	public void CreateMimeMessage_MarksMessageAutoGenerated_AndSetsNoPriorityHeader()
	{
		using var meterFactory = new TestMeterFactory();
		var sut = CreateService(GetUnusedLoopbackPort(), new EmailMetrics(meterFactory));

		using var mime = sut.CreateMimeMessage(Message("vera@example.com", "Subject", "corr"));

		mime.Headers["Auto-Submitted"].Should().Be("auto-generated");
		mime.Headers.Contains(HeaderId.XPriority).Should().BeFalse("live testers read every email as high priority (#2402)");
		mime.Headers.Contains(HeaderId.Importance).Should().BeFalse();
		mime.Headers.Contains(HeaderId.Priority).Should().BeFalse();
	}

	[Test]
	public void CreateMimeMessage_AddsListUnsubscribe_OnlyWhenTheEmailCanBeUnsubscribedFrom()
	{
		using var meterFactory = new TestMeterFactory();
		var sut = CreateService(GetUnusedLoopbackPort(), new EmailMetrics(meterFactory));

		using var optional = sut.CreateMimeMessage(
			Message("vera@example.com", "Reminder", "corr", unsubscribeUrl: "https://app.example/unsubscribe?x=1"));
		using var mandatory = sut.CreateMimeMessage(Message("vera@example.com", "Cancelled", "corr"));

		optional.Headers[HeaderId.ListUnsubscribe].Should().Be("<https://app.example/unsubscribe?x=1>");
		mandatory.Headers.Contains(HeaderId.ListUnsubscribe).Should().BeFalse();
	}

	private static EmailMessage Message(string to, string subject, string correlationId, string? unsubscribeUrl = null) =>
		new(to, new RenderedEmail(subject, "Text body", "<p>HTML body</p>", unsubscribeUrl), correlationId);

	private static SmtpEmailService CreateService(int port, EmailMetrics metrics, ILogger<SmtpEmailService>? logger = null)
	{
		var options = Options.Create(new SmtpOptions
		{
			Host = "127.0.0.1",
			Port = port,
			FromAddress = "noreply@test.local",
			FromName = "Test",
			EnableSsl = false,
		});

		return new SmtpEmailService(
			options,
			logger ?? NullLogger<SmtpEmailService>.Instance,
			metrics,
			new EmailRateLimiter(options));
	}

	private static List<(string Status, long Value)> RecordEmailSendMeasurements(IMeterFactory meterFactory)
	{
		var recorded = new List<(string, long)>();

		var listener = new MeterListener
		{
			InstrumentPublished = (instrument, l) =>
			{
				if (instrument.Meter.Name == EmailMetrics.MeterName)
					l.EnableMeasurementEvents(instrument);
			},
		};
		listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
		{
			var status = tags.ToArray()
				.FirstOrDefault(t => t.Key == "status").Value?.ToString()
				?? throw new InvalidOperationException("Measurement missing 'status' tag.");
			recorded.Add((status, measurement));
		});
		listener.Start();

		return recorded;
	}

	private static int GetUnusedLoopbackPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally
		{
			listener.Stop();
		}
	}

	private sealed class TestMeterFactory : IMeterFactory
	{
		private readonly List<Meter> _meters = [];

		public Meter Create(MeterOptions options)
		{
			var meter = new Meter(options);
			_meters.Add(meter);
			return meter;
		}

		public void Dispose()
		{
			foreach (var meter in _meters)
				meter.Dispose();
		}
	}

	private sealed class FakeSmtpServer : IAsyncDisposable
	{
		private readonly TcpListener _listener;
		private readonly Task _acceptTask;
		private readonly string? _rejectRecipient;

		private FakeSmtpServer(TcpListener listener, int port, string? rejectRecipient)
		{
			_listener = listener;
			Port = port;
			_rejectRecipient = rejectRecipient;
			_acceptTask = AcceptAsync();
		}

		public int Port { get; }

		public static Task<FakeSmtpServer> StartAsync(string? rejectRecipient = null)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			return Task.FromResult(new FakeSmtpServer(listener, port, rejectRecipient));
		}

		private async Task AcceptAsync()
		{
			using var client = await _listener.AcceptTcpClientAsync();
			await using var stream = client.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII);
			await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

			await writer.WriteLineAsync("220 fake.local ESMTP");

			string? line;
			while ((line = await reader.ReadLineAsync()) is not null)
			{
				if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
				{
					await writer.WriteLineAsync("250 fake.local");
				}
				else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
				{
					await writer.WriteLineAsync("250 OK");
				}
				else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
				{
					if (_rejectRecipient is not null &&
						line.Contains(_rejectRecipient, StringComparison.OrdinalIgnoreCase))
					{
						await writer.WriteLineAsync("550 Requested action not taken: mailbox unavailable");
					}
					else
					{
						await writer.WriteLineAsync("250 OK");
					}
				}
				else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
				{
					await writer.WriteLineAsync("354 Start mail input");
					while ((line = await reader.ReadLineAsync()) is not null && line != ".")
					{
					}

					await writer.WriteLineAsync("250 OK queued");
				}
				else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase))
				{
					await writer.WriteLineAsync("250 OK");
				}
				else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
				{
					await writer.WriteLineAsync("221 Bye");
					break;
				}
			}
		}

		public async ValueTask DisposeAsync()
		{
			_listener.Stop();
			try
			{
				await _acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (Exception)
			{
			}
		}
	}
}
