using AwesomeAssertions;

namespace IntegrationTests;

[ClassDataSource<IntegrationTestFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("IntegrationDb")]
public class CreateOrganizationTests(
	IntegrationTestFixture fixture)
{
	[Before(Test)]
	public Task ResetAsync() => fixture.ResetAsync();

	[Test]
	public async Task CreateOrganization_ShouldReturnOrganization_WhenNameIsValid(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var result = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Sample Fire Department" }, cancellationToken);

		result.Name.Should().Be("Sample Fire Department");
	}

	[Test]
	public async Task CreateOrganization_ShouldSucceed_WhenNameContainsGermanCharacters(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var result = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Ärztlicher Übungsdienst Straße" }, cancellationToken);

		result.Name.Should().Be("Ärztlicher Übungsdienst Straße");
	}

	[Test]
	public async Task CreateOrganization_ShouldSucceed_WhenNameContainsSpecialCharacters(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var result = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Org (Test) & Co. #1" }, cancellationToken);

		result.Name.Should().Be("Org (Test) & Co. #1");
	}

	[Test]
	public async Task CreateOrganization_ShouldReturn400_WhenAddressStreetExceedsMaxLength(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var act = () => client.CreateOrganizationAsync(
			new CreateOrganizationRequest
			{
				Name = "Org With Bad Address",
				Address = new CreateAddressRequest
				{
					Street = new string('a', 201),
					HouseNumber = "1",
					ZipCode = "12345",
					City = "Berlin",
				},
			},
			cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(400);
	}

	[Test]
	public async Task CreateOrganization_ShouldReturn401_WhenNotAuthenticated(
		CancellationToken cancellationToken)
	{
		var client = new AfuntoApi(fixture.CreateHttpClient());

		var act = () => client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Unauthorized Org" }, cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(401);
	}

	[Test]
	public async Task GetOrganizations_ShouldReturnCreatedOrganization(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Test Organization" }, cancellationToken);

		var result = await client.GetOrganizationsAsync(cancellationToken);

		result.Should().Contain(o => o.Name == "Test Organization" && o.Role == "Organizer");
	}

	// The header nav gates organizer-only sections outside the org app, where
	// no organization details response is loaded - so this list is the only
	// thing that can tell it a member is just a member (#2316).
	[Test]
	public async Task GetOrganizations_ShouldReportThePlainMemberRole_ForAnOrganizationTheUserOnlyBelongsTo(
		CancellationToken cancellationToken)
	{
		var olafClient = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");
		var veraClient = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");
		var vera = await veraClient.GetUserProfileAsync(cancellationToken);

		var org = await olafClient.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Role In My Organizations Test Org" }, cancellationToken);

		await fixture.AddPlainMemberDirectlyAsync(org.Id.Value, vera.Id, cancellationToken);

		var result = await veraClient.GetOrganizationsAsync(cancellationToken);

		result.Should().ContainSingle(o => o.Id == org.Id.Value)
			.Which.Role.Should().Be("Member");
	}

	[Test]
	public async Task GetOrganizations_ShouldReturnEmpty_WhenUserHasNoOrganizations(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("vera", "vera123");

		var result = await client.GetOrganizationsAsync(cancellationToken);

		result.Should().BeEmpty();
	}

	[Test]
	public async Task CreateOrganization_ShouldPersistEveryOptionalField_AndReturnThemFromDetails(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var created = await client.CreateOrganizationAsync(new CreateOrganizationRequest
		{
			Name = "Full Details Org",
			Description = "A helpful description for volunteers.",
			ContactEmail = "contact@example.com",
			ContactPhone = "+49 30 1234567",
			Website = "https://example.com",
			Address = new CreateAddressRequest
			{
				Street = "Main Street",
				HouseNumber = "1",
				ZipCode = "12345",
				City = "Berlin",
			},
		}, cancellationToken);

		var details = await client.GetOrganizationDetailsAsync(created.Id.Value, cancellationToken);

		details.Name.Should().Be("Full Details Org");
		details.Description.Should().Be("A helpful description for volunteers.");
		details.ContactEmail.Should().Be("contact@example.com");
		details.ContactPhone.Should().Be("+49 30 1234567");
		details.Website.Should().Be("https://example.com");
		details.Address.Should().NotBeNull();
		details.Address.Street.Should().Be("Main Street");
		details.Address.HouseNumber.Should().Be("1");
		details.Address.ZipCode.Should().Be("12345");
		details.Address.City.Should().Be("Berlin");
	}

	[Test]
	public async Task CreateOrganization_ShouldLeaveOptionalFieldsNull_WhenOnlyANameWasGiven(
		CancellationToken cancellationToken)
	{
		var client = await fixture.CreateAuthenticatedClientAsync("olaf", "olaf123");

		var created = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = "Name Only Org" }, cancellationToken);

		var details = await client.GetOrganizationDetailsAsync(created.Id.Value, cancellationToken);

		details.Name.Should().Be("Name Only Org");
		details.Description.Should().BeNull();
		details.ContactEmail.Should().BeNull();
		details.ContactPhone.Should().BeNull();
		details.Website.Should().BeNull();
		details.Address.Should().BeNull();
	}

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldReturn403_WhenUsingTheTokenFromBeforeOrganizationCreation(
		CancellationToken cancellationToken)
	{
		// Keycloak bakes role claims into the access token at issue time, so
		// the very token that just created the organization - and so far has
		// never held the organisator role - still doesn't hold it (afunto#2206).
		var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var client = await fixture.CreateAuthenticatedClientAsync(username, password);

		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Stale Token Org {Guid.NewGuid()}" }, cancellationToken);

		var act = () => client.CreateVolunteerOpportunityAsync(
			BuildOpportunityRequest(org.Id.Value), cancellationToken);

		var exception = await act.Should().ThrowAsync<ApiException>();
		exception.Which.StatusCode.Should().Be(403);
	}

	[Test]
	public async Task CreateVolunteerOpportunity_ShouldSucceed_WhenUsingATokenRefreshedAfterOrganizationCreation(
		CancellationToken cancellationToken)
	{
		// Refreshing the token - what the frontend's auth.signinSilent() does
		// right after organization creation - re-issues it with the
		// organisator role Keycloak just granted, so the caller's very next
		// request carries it (afunto#2206).
		var (_, username, password) = await fixture.CreateEphemeralUserAsync(cancellationToken);
		var client = await fixture.CreateAuthenticatedClientAsync(username, password);

		var org = await client.CreateOrganizationAsync(
			new CreateOrganizationRequest { Name = $"Refreshed Token Org {Guid.NewGuid()}" }, cancellationToken);

		client = await fixture.CreateAuthenticatedClientAsync(username, password);

		var opportunity = await client.CreateVolunteerOpportunityAsync(
			BuildOpportunityRequest(org.Id.Value), cancellationToken);

		opportunity.Should().NotBeNull();
	}

	private static CreateVolunteerOpportunityRequest BuildOpportunityRequest(Guid organizationId) =>
		new()
		{
			TitleDe = "Test Opportunity",
			DescriptionDe = "Integration test opportunity",
			OrganizationId = organizationId,
			Street = "Test Street",
			HouseNumber = "1",
			ZipCode = "12345",
			City = "Berlin",
			Occurrence = "Recurring",
			ParticipationType = "ScheduledSlots",
			CheckInMethod = "None",
			IsDraft = true,
		};
}
