import { useState, useRef, useEffect } from "react";
import { useAuth } from "react-oidc-context";
import { useAuthRoles } from "../../hooks/useAuthRoles";
import { useTranslation } from "react-i18next";
import { useLocation, Link } from "react-router";
import OrganizationSwitcher from "./OrganizationSwitcher";
import { useAccountMenu } from "../../hooks/useAccountMenu";
import { useAuthDisplayStatus } from "../../hooks/useAuthDisplayStatus";
import { useMyOrganizations } from "../../hooks/useMyOrganizations";
import { signinRedirectForRegistration } from "../../lib/keycloakRegistration";
import { signinLocaleArgs } from "../../lib/authLocale";
import { clearActiveOrgId } from "../../lib/activeOrg";
import {
	clearAuthRecoveryAttempts,
	recordAuthRecoveryAttempt,
} from "../../lib/authRecovery";
import { clearSeenAchievements } from "../../hooks/useAchievementNotifier";
import { useDisplayName } from "../../hooks/useDisplayName";
import { clearDisplayNameOverride } from "../../lib/displayName";
import { getInitials } from "../../lib/initials";
import DesktopHeader from "./DesktopHeader";
import MobileHeader from "./MobileHeader";
import MobileMenu from "./MobileMenu";

export default function Header({
	orgSwitcher,
	overlaysBand = false,
}: {
	orgSwitcher?: { currentOrgId: string; currentTab: string };

	overlaysBand?: boolean;
} = {}) {
	const auth = useAuth();
	const { t } = useTranslation();
	const location = useLocation();
	const authStatus = useAuthDisplayStatus();
	const isLoggedIn = authStatus === "signedIn";
	const user = auth.user?.profile;
	// Keycloak re-issues the id_token's `name` only on the next sign-in, so a
	// name saved on /profile is picked up from the override the profile page
	// publishes until a fresh token carries it (#2330).
	const claimedName = (user?.name ??
		user?.preferred_username ??
		"User") as string;
	const displayName = useDisplayName(user?.sub, claimedName);
	const initials = isLoggedIn ? getInitials(displayName) : "";
	const isAdmin = useAuthRoles().has("admin");

	const {
		orgs,
		activeOrg,
		loading: orgsLoading,
		error: orgsError,
	} = useMyOrganizations();

	// A member of more than one organization needs a way to switch between
	// them from outside the org app too (#2226), not just once they're
	// already inside /app/:orgId/*. Below lg:, that's the only org control
	// in the collapsed header at all (the rest lives a tap deep in the
	// hamburger menu), so the fallback switcher pill renders there; at lg:+
	// the existing primary-nav "go to my org" entry already fills that role
	// and there isn't room for both without overflowing the row (see the
	// lg:hidden below), so the fallback stays mobile/tablet-only.
	const effectiveOrgSwitcher =
		orgSwitcher ??
		(orgs.length > 1 && activeOrg
			? { currentOrgId: activeOrg.id, currentTab: "dashboard" }
			: undefined);
	// Only the org app's own route context makes the primary-nav "go to my
	// org" entry redundant (its own in-app navigation already covers that).
	// Outside the app, keep it even once the switcher fallback above also
	// renders - for a multi-org user it's still the one place the mobile
	// menu offers direct links into the org's sub-tabs.
	const navOrg = orgSwitcher ? null : activeOrg;
	// Inside the org app the row also carries the org switcher, which needs
	// ~290px next to the 158px logo. The German primary nav plus the account
	// controls and the language switcher then need more room than a 1024px
	// viewport has, and the overflow pushed the notification bell, the
	// account menu and the language switcher entirely off-screen - reachable
	// only by scrolling the whole page sideways (#2321). Hold the collapsed
	// header one breakpoint longer there, so every control in the row stays
	// reachable; the hamburger already carries the same links.
	const desktopNavFrom = orgSwitcher ? "xl" : "lg";
	const desktopNavClass = orgSwitcher ? "hidden xl:flex" : "hidden lg:flex";
	const mobileNavClass = orgSwitcher ? "xl:hidden" : "lg:hidden";
	const [mobileOpen, setMobileOpen] = useState(false);
	const [scrolled, setScrolled] = useState(false);
	const mobileNotifRef = useRef<HTMLDivElement>(null);
	const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
	const menu = useAccountMenu([mobileNotifRef]);
	const { avatarUrl } = menu;

	useEffect(() => {
		const onScroll = () => setScrolled(window.scrollY > 100);
		window.addEventListener("scroll", onScroll, { passive: true });
		return () => window.removeEventListener("scroll", onScroll);
	}, []);

	useEffect(() => {
		const desktop = window.matchMedia(
			desktopNavFrom === "xl" ? "(min-width: 1280px)" : "(min-width: 1024px)",
		);
		const closeIfDesktop = () => {
			if (desktop.matches) setMobileOpen(false);
		};
		desktop.addEventListener("change", closeIfDesktop);
		return () => desktop.removeEventListener("change", closeIfDesktop);
	}, [desktopNavFrom]);

	const isTransparent = overlaysBand && !scrolled;

	function handleSignIn() {
		// Counts toward the bounded recovery (#2208) only when it recovers an
		// ended session - a first sign-in starts no episode.
		if (authStatus === "sessionExpired") recordAuthRecoveryAttempt();
		auth.signinRedirect({
			...signinLocaleArgs(),
			state: { returnTo: location.pathname + location.search },
		});
	}

	function handleRegister() {
		void signinRedirectForRegistration({
			...signinLocaleArgs(),
			state: { returnTo: location.pathname + location.search },
		});
	}

	function handleSignOut() {
		clearActiveOrgId();
		clearSeenAchievements(user?.sub);
		clearAuthRecoveryAttempts();
		clearDisplayNameOverride();
		auth.signoutRedirect();
	}

	return (
		<>
			<header
				className={`sticky top-0 z-40 motion-safe:transition-[background-color,box-shadow] motion-safe:duration-300 ${
					isTransparent && mobileOpen
						? "border-b-0 bg-brand-800"
						: isTransparent
							? "border-b-0 bg-transparent"
							: scrolled
								? "border-b border-transparent bg-white/95 shadow-md backdrop-blur-sm"
								: "border-b border-gray-200 bg-white"
				}`}
			>
				<div className="mx-auto max-w-page px-4 sm:px-6 lg:px-8">
					<div
						className={`flex h-16 items-center justify-between ${effectiveOrgSwitcher ? "gap-3 sm:gap-4" : ""}`}
					>
						{/* The logo is 32px tall, so the link around it was a 32px tap
						target; `min-h-11` adds hit area only - the mark itself does not
						grow (#2327). */}
						<Link
							to="/"
							className={`flex min-h-11 shrink-0 items-center ${effectiveOrgSwitcher ? "w-8 overflow-hidden sm:w-auto sm:overflow-visible" : ""}`}
						>
							<img
								src="/logo.svg"
								alt={t("brand.name")}
								className={`h-8 w-auto max-w-none shrink-0 motion-safe:transition-[filter] motion-safe:duration-300 ${isTransparent ? "brightness-0 invert" : ""}`}
							/>
						</Link>

						{effectiveOrgSwitcher && (
							<div
								className={`min-w-0 flex-1 sm:max-w-72 sm:min-w-48 sm:flex-none sm:shrink xl:max-w-64 2xl:max-w-none ${orgSwitcher ? "" : "lg:hidden"}`}
							>
								<OrganizationSwitcher
									currentOrgId={effectiveOrgSwitcher.currentOrgId}
									currentTab={effectiveOrgSwitcher.currentTab}
									orgs={orgs}
									loading={orgsLoading}
									error={orgsError}
									transparent={isTransparent}
								/>
							</div>
						)}

						<DesktopHeader
							className={desktopNavClass}
							authStatus={authStatus}
							isTransparent={isTransparent}
							menu={menu}
							displayName={displayName}
							initials={initials}
							isAdmin={isAdmin}
							activeOrg={navOrg}
							onSignOut={handleSignOut}
							onSignIn={handleSignIn}
							onRegister={handleRegister}
						/>

						<MobileHeader
							className={mobileNavClass}
							isLoggedIn={isLoggedIn}
							isTransparent={isTransparent}
							mobileOpen={mobileOpen}
							setMobileOpen={setMobileOpen}
							menu={menu}
							notifContainerRef={mobileNotifRef}
							menuButtonRef={mobileMenuButtonRef}
						/>
					</div>
				</div>

				{mobileOpen && (
					<MobileMenu
						className={mobileNavClass}
						isTransparent={isTransparent}
						authStatus={authStatus}
						avatarUrl={avatarUrl}
						initials={initials}
						displayName={displayName}
						isAdmin={isAdmin}
						activeOrg={navOrg}
						triggerRef={mobileMenuButtonRef}
						onClose={() => setMobileOpen(false)}
						onSignIn={handleSignIn}
						onRegister={handleRegister}
						onSignOut={handleSignOut}
					/>
				)}
			</header>
		</>
	);
}
