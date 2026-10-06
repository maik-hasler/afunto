import { describe, it, expect, beforeEach, vi } from "vitest";

const { UserManagerMock, WebStorageStateStoreMock, signinRedirectMock } =
	vi.hoisted(() => {
		const signinRedirectMock = vi.fn().mockResolvedValue(undefined);
		const UserManagerMock = vi.fn().mockImplementation(function (
			options: unknown,
		) {
			return { signinRedirect: signinRedirectMock, options };
		});
		const WebStorageStateStoreMock = vi.fn().mockImplementation(function (
			options: unknown,
		) {
			return { options };
		});
		return { UserManagerMock, WebStorageStateStoreMock, signinRedirectMock };
	});

vi.mock("oidc-client-ts", () => ({
	UserManager: UserManagerMock,
	WebStorageStateStore: WebStorageStateStoreMock,
}));

vi.mock("./runtimeConfig", () => ({
	runtimeConfig: {
		keycloakAuthorityUrl: "https://keycloak.example/realms/afunto",
		keycloakClientId: "frontend",
		apiUrl: "https://api.example",
	},
}));

describe("signinRedirectForRegistration", () => {
	beforeEach(() => {
		vi.resetModules();
		UserManagerMock.mockClear();
		WebStorageStateStoreMock.mockClear();
		signinRedirectMock.mockClear();
	});

	it("points the UserManager's authorization_endpoint at the registrations endpoint", async () => {
		const { signinRedirectForRegistration } =
			await import("./keycloakRegistration");
		await signinRedirectForRegistration();

		expect(UserManagerMock).toHaveBeenCalledTimes(1);
		const options = UserManagerMock.mock.calls[0][0] as {
			authority: string;
			client_id: string;
			scope: string;
			redirect_uri: string;
			metadataSeed: { authorization_endpoint: string };
		};
		expect(options.authority).toBe("https://keycloak.example/realms/afunto");
		expect(options.client_id).toBe("frontend");
		expect(options.scope).toBe("openid profile email");
		expect(options.redirect_uri).toBe(`${window.location.origin}/callback`);
		expect(options.metadataSeed.authorization_endpoint).toBe(
			"https://keycloak.example/realms/afunto/protocol/openid-connect/registrations",
		);
	});

	it("reuses the same UserManager instance across repeated calls", async () => {
		const { signinRedirectForRegistration } =
			await import("./keycloakRegistration");
		await signinRedirectForRegistration();
		await signinRedirectForRegistration();

		expect(UserManagerMock).toHaveBeenCalledTimes(1);
		expect(signinRedirectMock).toHaveBeenCalledTimes(2);
	});

	it("forwards the extra signin args to signinRedirect", async () => {
		const { signinRedirectForRegistration } =
			await import("./keycloakRegistration");
		await signinRedirectForRegistration({ ui_locales: "de" });

		expect(signinRedirectMock).toHaveBeenCalledWith({ ui_locales: "de" });
	});
});
