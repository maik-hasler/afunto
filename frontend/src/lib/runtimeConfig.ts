interface AppConfig {
	KEYCLOAK_AUTHORITY_URL: string;
	KEYCLOAK_CLIENT_ID: string;
	API_URL: string;
	TOAST_LIFETIME_MS: string;
	APP_VERSION: string;
	OPERATOR_NAME: string;
	OPERATOR_ADDRESS: string;
	OPERATOR_EMAIL: string;
	OPERATOR_SITE_URL: string;
}

declare global {
	interface Window {
		__APP_CONFIG__?: Partial<AppConfig>;
	}
}

function resolve(key: keyof AppConfig, fallback: string): string {
	const value = window.__APP_CONFIG__?.[key];
	if (value && !value.startsWith("${")) {
		return value;
	}
	return fallback;
}

const keycloakAuthorityUrl = resolve(
	"KEYCLOAK_AUTHORITY_URL",
	import.meta.env.VITE_KEYCLOAK_AUTHORITY_URL,
);
const keycloakClientId = resolve(
	"KEYCLOAK_CLIENT_ID",
	import.meta.env.VITE_KEYCLOAK_CLIENT_ID,
);
const apiUrl = resolve("API_URL", import.meta.env.VITE_API_URL);
const appVersion = resolve(
	"APP_VERSION",
	import.meta.env.VITE_APP_VERSION ?? "dev",
);
const operatorName = resolve("OPERATOR_NAME", "");
const operatorAddress = resolve("OPERATOR_ADDRESS", "");
const operatorEmail = resolve("OPERATOR_EMAIL", "");
const operatorSiteUrl = resolve("OPERATOR_SITE_URL", "");

export const runtimeConfig = {
	keycloakAuthorityUrl,
	keycloakClientId,
	apiUrl,
	appVersion,

	toastLifetimeMs: Number(
		resolve(
			"TOAST_LIFETIME_MS",
			import.meta.env.VITE_TOAST_LIFETIME_MS ?? "5000",
		),
	),

	operatorName,
	operatorAddress,
	operatorEmail,
	operatorSiteUrl,
	// DDG §5/GDPR Art. 13 need a complete legal identity - a half-filled notice
	// (e.g. a name with no way to reach them) is worse than none, so this is
	// all-or-nothing rather than per-field (afunto#2196).
	operatorConfigured: Boolean(
		operatorName && operatorAddress && operatorEmail && operatorSiteUrl,
	),

	// False when neither the container's /config.js nor the image's own
	// build-time fallback provided a real value for one of the three fields
	// above (#2207) - e.g. an offline PWA cold start that never reached
	// /config.js, on an image built without the (now default-less) VITE_*
	// build args. ConfigGate (src/components/ConfigGate.tsx) refuses to
	// render the app in that state instead of quietly running against an
	// empty API/Keycloak origin.
	isConfigured: Boolean(keycloakAuthorityUrl && keycloakClientId && apiUrl),
};
