import { lazy, useEffect, type ReactNode } from "react";
import {
	Routes,
	Route,
	Navigate,
	Outlet,
	useOutletContext,
	useSearchParams,
} from "react-router";
import { useAuth } from "react-oidc-context";
import { useTranslation } from "react-i18next";
import { useDocumentMetaDefaults } from "./hooks/useDocumentMetaDefaults";
import { useSessionExpiryHandler } from "./hooks/useSessionExpiryHandler";
import { useSilentSsoProbe } from "./hooks/useSilentSsoProbe";
import {
	AuthStatusProvider,
	useAuthRecoveryFailedFlag,
} from "./contexts/AuthStatusContext";
import { signinLocaleArgs } from "./lib/authLocale";
import { clearAuthRecoveryAttempts } from "./lib/authRecovery";
import ErrorBanner from "./components/ErrorBanner";
import Button from "./components/Button";
import RouteAnnouncer from "./components/RouteAnnouncer";
import HashScroller from "./components/HashScroller";
import RouteState from "./components/RouteState";
import { SpinnerIcon } from "./components/Spinner";
import { statusTitleClass } from "./lib/headingClasses";
import AppLayout from "./layouts/AppLayout";
import ProtectedRoute from "./layouts/ProtectedRoute";
import OrgAppLayout, { type OrgAppContext } from "./layouts/OrgAppLayout";
import HomePage from "./pages/HomePage";

import NotFoundPage from "./pages/NotFoundPage";

const OpportunitiesPage = lazy(() => import("./pages/OpportunitiesPage"));
const PrivacyPolicyPage = lazy(() => import("./pages/PrivacyPolicyPage"));
const ImprintPage = lazy(() => import("./pages/ImprintPage"));
const TermsOfUsePage = lazy(() => import("./pages/TermsOfUsePage"));
const ContactPage = lazy(() => import("./pages/ContactPage"));
const HelpPage = lazy(() => import("./pages/HelpPage"));
const UnsubscribePage = lazy(() => import("./pages/UnsubscribePage"));
const VolunteerOpportunityDetailPage = lazy(
	() => import("./pages/VolunteerOpportunityDetailPage"),
);
const EngagementManagementPage = lazy(
	() => import("./pages/EngagementManagementPage"),
);
const ProfileOverviewPage = lazy(() => import("./pages/ProfileOverviewPage"));
const MyEngagementsPage = lazy(() => import("./pages/MyEngagementsPage"));
const OrganizationProfilePage = lazy(
	() => import("./pages/OrganizationProfilePage"),
);
const OrganizationsPage = lazy(() => import("./pages/OrganizationsPage"));
// One chunk per admin route, not four routes sharing one. All five used to
// resolve to the same module specifier, so opening the audit log downloaded
// the organization, user and report screens with it.
const AdministrationPage = lazy(
	() => import("./pages/administration/AdministrationPage"),
);
const AdminOrganizationsPage = lazy(
	() => import("./pages/administration/AdminOrganizationsPage"),
);
const AdminUsersPage = lazy(
	() => import("./pages/administration/AdminUsersPage"),
);
const AdminReportsPage = lazy(
	() => import("./pages/administration/AdminReportsPage"),
);
const AdminAuditLogPage = lazy(
	() => import("./pages/administration/AdminAuditLogPage"),
);
const UserProfilePage = lazy(() => import("./pages/UserProfilePage"));
const OrgDashboardPage = lazy(() => import("./pages/app/OrgDashboardPage"));
const OrgOpportunitiesPage = lazy(
	() => import("./pages/app/OrgOpportunitiesPage"),
);
const OrgEngagementsPage = lazy(() => import("./pages/app/OrgEngagementsPage"));
const OrgMembersPage = lazy(() => import("./pages/app/OrgMembersPage"));
const OrgSettingsPage = lazy(() => import("./pages/app/OrgSettingsPage"));

function OrgAppOutletRelay() {
	const context = useOutletContext<OrgAppContext>();
	return <Outlet context={context} />;
}

function CallbackPage() {
	const auth = useAuth();
	const { t } = useTranslation();
	const [searchParams] = useSearchParams();

	// Keycloak's own reason for refusing, when it sent one. It is the actual
	// cause, and more useful than whatever oidc-client-ts then failed on.
	const providerError = searchParams.get("error");
	// Keycloak always comes back here carrying one of these two. A bare
	// /callback (typed, bookmarked, or shared) carries neither, oidc-client-ts
	// has nothing to resolve, and the "completing sign-in" line used to sit
	// there indefinitely (#2320). A lone `state` does not count: it is not a
	// completable response either, so it belongs on the same way out.
	const isSigninResponse = searchParams.has("code") || providerError !== null;

	// Kept out of the copy on purpose: oidc-client-ts's messages are
	// untranslated internals ("No matching state found in storage") that mean
	// nothing to a signed-out visitor, but everything to whoever debugs this.
	useEffect(() => {
		if (auth.error)
			console.error("[auth] sign-in callback failed:", auth.error.message);
		if (providerError)
			console.error("[auth] identity provider refused:", providerError);
	}, [auth.error, providerError]);

	// No returnTo here deliberately: we're on /callback, which is never a
	// meaningful place to send the user back to - Keycloak always redirects
	// here regardless of where signin started (#2223).
	const retry = () => void auth.signinRedirect(signinLocaleArgs());
	const backToSite = { to: "/", label: t("orgApp.backToSite") };

	function stateScreen(node: ReactNode) {
		return (
			<main className="flex min-h-screen flex-col justify-center">{node}</main>
		);
	}

	if (auth.error || providerError) {
		return stateScreen(
			<RouteState
				// "error" even when the provider says the user declined: it is the
				// one variant RouteState offers a retry on (#1774), and a retry is
				// exactly what someone who cancelled by accident needs.
				variant="error"
				title={t("auth.signInFailedTitle")}
				message={
					providerError === "access_denied"
						? t("auth.signInDeclined")
						: t("auth.signInFailedMessage")
				}
				onRetry={retry}
				action={backToSite}
				data-testid="callback-error"
			/>,
		);
	}

	if (!isSigninResponse && !auth.isLoading) {
		return stateScreen(
			<RouteState
				variant="notFound"
				title={t("auth.nothingToCompleteTitle")}
				message={t("auth.nothingToCompleteMessage")}
				action={backToSite}
				data-testid="callback-nothing-to-complete"
			/>,
		);
	}

	// The heading carries the message, so the spinner beside it stays purely
	// decorative rather than repeating "loading" in a second register.
	return (
		<main className="flex min-h-screen flex-col items-center justify-center gap-6 px-4 text-center">
			<h1 className={`text-gray-900 ${statusTitleClass}`}>
				{t("auth.completing")}
			</h1>
			<SpinnerIcon className="h-8 w-8" />
		</main>
	);
}

// The bounded-redirect-loop terminal state (#2208): useSessionExpiryHandler
// gives up after a second consecutive session expiry with no successful API
// call in between (e.g. a backend ValidIssuers mismatch that no amount of
// re-authenticating can fix), rather than keep bouncing the visitor through
// Keycloak. Rendered in place of the whole route tree - a page-scoped state
// wouldn't be reachable if the failure happens before that page's own data
// ever loads, and the user needs a guaranteed way out regardless of route.
function AuthRecoveryFailedPage() {
	const auth = useAuth();
	const { t } = useTranslation();

	function handleSignOut() {
		clearAuthRecoveryAttempts();
		auth.signoutRedirect();
	}

	return (
		<main className="flex min-h-screen flex-col items-center justify-center gap-4 px-4 text-center">
			<h1 className="text-xl font-semibold text-gray-900">
				{t("auth.recoveryFailedTitle")}
			</h1>
			<ErrorBanner message={t("auth.recoveryFailedMessage")} />
			<Button onClick={handleSignOut}>{t("nav.signOut")}</Button>
		</main>
	);
}

// Exported for App.test.tsx: rendering this directly (instead of <App/>)
// avoids App's own <AuthStatusProvider> below shadowing the one
// renderWithProviders sets up for the test.
export function AppRoutes() {
	useDocumentMetaDefaults();
	useSessionExpiryHandler();
	useSilentSsoProbe();
	const authRecoveryFailed = useAuthRecoveryFailedFlag();

	if (authRecoveryFailed) return <AuthRecoveryFailedPage />;

	return (
		<>
			<RouteAnnouncer />
			<HashScroller />
			<Routes>
				<Route path="/callback" element={<CallbackPage />} />
				<Route
					path="/app/:organizationId"
					element={
						<ProtectedRoute>
							<OrgAppLayout />
						</ProtectedRoute>
					}
				>
					<Route index element={<Navigate to="dashboard" replace />} />

					<Route path="dashboard" element={<OrgAppOutletRelay />}>
						<Route index element={<OrgDashboardPage />} />
						<Route path="opportunities" element={<OrgOpportunitiesPage />} />
						<Route
							path="opportunities/:opportunityId/engagements"
							element={<EngagementManagementPage />}
						/>
						<Route path="engagements" element={<OrgEngagementsPage />} />
						<Route path="members" element={<OrgMembersPage />} />
						<Route path="settings" element={<OrgSettingsPage />} />
					</Route>
				</Route>
				<Route element={<AppLayout />}>
					<Route path="/" element={<HomePage />} />

					<Route path="/opportunities" element={<OpportunitiesPage />} />
					<Route path="/privacy-policy" element={<PrivacyPolicyPage />} />
					<Route path="/imprint" element={<ImprintPage />} />
					<Route path="/terms-of-use" element={<TermsOfUsePage />} />
					<Route path="/contact" element={<ContactPage />} />
					<Route path="/help" element={<HelpPage />} />
					{/* Links in emails sent before the backend stopped sending any. */}
					<Route path="/unsubscribe" element={<UnsubscribePage />} />
					<Route path="/unsubscribed" element={<UnsubscribePage />} />
					<Route
						path="/volunteer-opportunities/:opportunityId"
						element={<VolunteerOpportunityDetailPage />}
					/>
					<Route path="/organizations" element={<OrganizationsPage />} />
					<Route
						path="/organizations/:organizationId"
						element={<OrganizationProfilePage />}
					/>

					<Route
						path="/my-signups"
						element={
							<ProtectedRoute>
								<MyEngagementsPage />
							</ProtectedRoute>
						}
					/>
					<Route
						path="/profile"
						element={
							<ProtectedRoute>
								<ProfileOverviewPage />
							</ProtectedRoute>
						}
					/>
					{/* /profile/settings was merged into /profile - keep the old
					path resolving for bookmarks and links in past emails (#2354). */}
					<Route
						path="/profile/settings"
						element={<Navigate to="/profile" replace />}
					/>
					<Route path="/users/:userId" element={<UserProfilePage />} />

					<Route
						path="/administration"
						element={
							<ProtectedRoute requiredRole="admin">
								<AdministrationPage />
							</ProtectedRoute>
						}
					>
						<Route index element={<Navigate to="organizations" replace />} />
						<Route path="organizations" element={<AdminOrganizationsPage />} />
						<Route path="users" element={<AdminUsersPage />} />
						<Route path="reports" element={<AdminReportsPage />} />
						<Route path="audit-log" element={<AdminAuditLogPage />} />
					</Route>
					<Route path="*" element={<NotFoundPage />} />
				</Route>
			</Routes>
		</>
	);
}

export default function App() {
	return (
		<AuthStatusProvider>
			<AppRoutes />
		</AuthStatusProvider>
	);
}
