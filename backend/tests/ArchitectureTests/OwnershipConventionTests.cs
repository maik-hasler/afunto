using Application.Common.Messaging;
using AwesomeAssertions;
using NetArchTest.Rules;

namespace ArchitectureTests;

public sealed class OwnershipConventionTests
{
	private static readonly HashSet<string> AllowListedExceptions = new()
	{
		"Application.VolunteerOpportunities.GetVolunteerOpportunityDetails.v1.GetVolunteerOpportunityDetailsQueryHandler",
		// Public listings carry the optional viewer so members-only opportunities show up
		// for the members of the owning organization and for nobody else; there is no
		// organization to guard, the read repository filters by membership.
		"Application.VolunteerOpportunities.GetVolunteerOpportunities.v1.GetVolunteerOpportunitiesQueryHandler",
		"Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1.GetVolunteerOpportunityDateAvailabilityQueryHandler",

		"Application.Notifications.MarkNotificationRead.v1.MarkNotificationReadCommandHandler",
		"Application.Notifications.MarkNotificationUnread.v1.MarkNotificationUnreadCommandHandler",
		"Application.Notifications.DeleteNotification.v1.DeleteNotificationCommandHandler",

		"Application.Engagements.SubmitFeedback.v1.SubmitFeedbackCommandHandler",
		"Application.Engagements.UpdateFeedback.v1.UpdateFeedbackCommandHandler",
		"Application.Engagements.DeleteFeedback.v1.DeleteFeedbackCommandHandler",
		"Application.Engagements.CheckInWithPin.v1.CheckInWithPinCommandHandler",
	};

	[Test]
	public void OrgScopedHandlers_ShouldCallOwnershipGuard()
	{
		var handlersWithOwnershipGuard = Types
			.InAssembly(AssemblyAnchors.ApplicationLayer)
			.That()
			.HaveDependencyOn("Application.Common.Authorization.OwnershipGuard")
			.GetTypes()
			.Select(t => t.FullName)
			.ToHashSet();

		var violators = TypeDiscovery
			.GetImplementationPairs(AssemblyAnchors.ApplicationLayer, typeof(ICommandHandler<,>), typeof(IQueryHandler<,>))
			.Where(pair => pair.FirstTypeArg.GetProperty("RequestingUserId") is not null)
			.Select(pair => pair.Implementation)
			.Where(handler => !handlersWithOwnershipGuard.Contains(handler.FullName))
			.Where(handler => !AllowListedExceptions.Contains(handler.FullName!))
			.Select(handler => handler.FullName)
			.ToList();

		violators.Should().BeEmpty(
			"a handler whose command/query carries RequestingUserId is presumed to be organization-scoped " +
			"and must call OwnershipGuard.EnsureIsOrganizerAsync, or a new endpoint can ship with no tenant " +
			"check at all - if this is a deliberate exception (e.g. a per-user resource check instead of an " +
			"organization one), add it to OwnershipConventionTests.AllowListedExceptions with a comment " +
			"explaining why");
	}
}
