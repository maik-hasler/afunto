import i18n from "./i18n";
import React, { Suspense } from "react";
import ReactDOM from "react-dom/client";
import { AuthProvider } from "react-oidc-context";
import { type User } from "oidc-client-ts";
import { BrowserRouter } from "react-router";
import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import ConfigGate from "./components/ConfigGate";
import ErrorBoundary from "./components/ErrorBoundary";
import PwaUpdatePrompt from "./components/PwaUpdatePrompt";
import { SpinnerIcon } from "./components/Spinner";
import { ToastProvider } from "./contexts/ToastContext";
import { runtimeConfig } from "./lib/runtimeConfig";
import { dispatchToast } from "./lib/toastBus";
import { handleUnhandledRejection } from "./lib/unhandledRejection";
import { createQueryClient } from "./queryClient";
import { createTokenStore } from "./lib/tokenStorage";

import "react-big-calendar/lib/css/react-big-calendar.css";
import "@fontsource-variable/source-sans-3";

import "@fontsource/barlow-condensed/700.css";
import "./styles/global.css";

// One cache for the lifetime of the tab. Created out here rather than inside a
// component so a re-render can never swap it and throw the whole cache away.
const queryClient = createQueryClient();

const oidcConfig = {
	authority: runtimeConfig.keycloakAuthorityUrl,
	client_id: runtimeConfig.keycloakClientId,
	redirect_uri: window.location.origin + "/callback",
	post_logout_redirect_uri: window.location.origin,
	scope: "openid profile email",
	automaticSilentRenew: true,

	silent_redirect_uri: window.location.origin + "/silent-renew.html",
	// See lib/tokenStorage.ts for why this is sessionStorage.
	userStore: createTokenStore(),
	onSigninCallback: async (user: User | undefined) => {
		const hasExplicitLanguageChoice =
			localStorage.getItem("afunto:language-explicit") === "true";
		const keycloakLocale = user?.profile?.locale;
		if (
			!hasExplicitLanguageChoice &&
			keycloakLocale &&
			keycloakLocale !== i18n.language
		) {
			dispatchToast(
				"info",
				i18n.t("language.switchedFromAccount", {
					language: i18n.t(`language.${keycloakLocale}`),
				}),
			);
			await i18n.changeLanguage(keycloakLocale);

			await new Promise((resolve) => setTimeout(resolve, 2000));
		}
		const returnTo = (user?.state as { returnTo?: string })?.returnTo ?? "/";
		window.location.replace(returnTo);
	},
};

window.addEventListener("unhandledrejection", (event) => {
	handleUnhandledRejection(event.reason);
});

// Hands over from index.html's app shell, which React clears on its first
// commit: locales and the first route chunk are still in flight at that point,
// and a `null` fallback turned the shell straight back into a blank white
// page. Deliberately free of `t()` - i18n is one of the things suspending
// here - and aria-hidden, since a translated status has nothing to say yet
// (#2320).
function AppBoot() {
	return (
		<div
			aria-hidden="true"
			className="flex min-h-screen items-center justify-center bg-brand-50"
		>
			<SpinnerIcon className="h-12 w-12" />
		</div>
	);
}

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
	<React.StrictMode>
		<PwaUpdatePrompt />
		<ErrorBoundary>
			<ConfigGate>
				<Suspense fallback={<AppBoot />}>
					<ToastProvider>
						<AuthProvider {...oidcConfig}>
							{/* Inside AuthProvider: a query's fetcher reads the current
							bearer token through useApiClient(), which needs the auth
							context above it. */}
							<QueryClientProvider client={queryClient}>
								<BrowserRouter>
									<App />
								</BrowserRouter>
							</QueryClientProvider>
						</AuthProvider>
					</ToastProvider>
				</Suspense>
			</ConfigGate>
		</ErrorBoundary>
	</React.StrictMode>,
);
