using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace IntegrationTests.Email;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
public class RealmSmtpDeliveryTests(IntegrationTestFixture fixture)
{
	private const string Realm = "afunto";

	[Test]
	public async Task SendVerifyEmail_DeliversToMailpit_ThroughRealmSmtpConfig(
		CancellationToken cancellationToken)
	{
		var email = $"emaildelivery-{Guid.NewGuid():N}@example.test";
		using var admin = await fixture.CreateKeycloakAdminClientAsync();

		var createResponse = await admin.PostAsJsonAsync($"/admin/realms/{Realm}/users", new
		{
			username = $"emaildelivery-{Guid.NewGuid():N}",
			email,
			enabled = true,
			emailVerified = false,
			requiredActions = new[] { "VERIFY_EMAIL" },
			credentials = new[]
			{
				new { type = "password", value = $"Test1070!{Guid.NewGuid():N}", temporary = false },
			},
		}, cancellationToken);
		createResponse.EnsureSuccessStatusCode();
		var userId = createResponse.Headers.Location!.Segments[^1];

		try
		{
			var sendResponse = await admin.PutAsync(
				$"/admin/realms/{Realm}/users/{userId}/send-verify-email",
				content: null,
				cancellationToken);
			sendResponse.EnsureSuccessStatusCode();

			// The subject is the email theme's (keycloak/themes/afunto/email), not Keycloak's
			// stock "E-Mail verifizieren" - proving the realm selects the theme at all (#2402).
			await AssertMailpitReceivedMessageToAsync(email, "E-Mail-Adresse bestätigen", cancellationToken);
		}
		finally
		{
			await admin.DeleteAsync($"/admin/realms/{Realm}/users/{userId}", cancellationToken);
		}
	}

	private async Task AssertMailpitReceivedMessageToAsync(
		string recipientEmail, string expectedSubject, CancellationToken cancellationToken)
	{
		using var mailpit = fixture.CreateMailpitClient();
		var deadline = DateTime.UtcNow.AddSeconds(30);

		while (DateTime.UtcNow < deadline)
		{
			var response = await mailpit.GetAsync("/api/v1/messages?limit=50", cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
				if (body.TryGetProperty("messages", out var messages)
					&& messages.EnumerateArray().Any(message =>
						message.TryGetProperty("To", out var to)
						&& to.EnumerateArray().Any(recipient =>
							recipient.TryGetProperty("Address", out var address)
							&& string.Equals(
								address.GetString(), recipientEmail, StringComparison.OrdinalIgnoreCase))
						&& message.TryGetProperty("Subject", out var subject)
						&& subject.GetString() == expectedSubject))
				{
					return;
				}
			}

			await Task.Delay(500, cancellationToken);
		}

		throw new Exception(
			$"Mailpit never received a message addressed to {recipientEmail} with subject \"{expectedSubject}\" within 30s - "
			+ "either the realm's SMTP config is not delivering, or the realm no longer uses the afunto email theme.");
	}
}
