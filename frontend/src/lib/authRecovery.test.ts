import { describe, it, expect, beforeEach } from "vitest";
import {
	recordAuthRecoveryAttempt,
	clearAuthRecoveryAttempts,
	hasExhaustedAuthRecovery,
	AUTH_RECOVERY_REDIRECT_LIMIT,
} from "./authRecovery";

beforeEach(() => {
	sessionStorage.clear();
});

describe("authRecovery", () => {
	it("starts at 1 on the first recorded attempt", () => {
		expect(recordAuthRecoveryAttempt()).toBe(1);
	});

	it("increments on each subsequent attempt", () => {
		recordAuthRecoveryAttempt();
		expect(recordAuthRecoveryAttempt()).toBe(2);
		expect(recordAuthRecoveryAttempt()).toBe(3);
	});

	it("persists the count across separate calls, as it must survive a full-page redirect", () => {
		recordAuthRecoveryAttempt();
		const secondReadBack = recordAuthRecoveryAttempt();
		expect(secondReadBack).toBe(2);
	});

	it("exceeds the redirect limit only once the limit has been used up", () => {
		for (let i = 0; i < AUTH_RECOVERY_REDIRECT_LIMIT; i++) {
			expect(recordAuthRecoveryAttempt()).toBeLessThanOrEqual(
				AUTH_RECOVERY_REDIRECT_LIMIT,
			);
		}
		expect(recordAuthRecoveryAttempt()).toBeGreaterThan(
			AUTH_RECOVERY_REDIRECT_LIMIT,
		);
	});

	it("resets back to 1 after clearing", () => {
		recordAuthRecoveryAttempt();
		recordAuthRecoveryAttempt();
		clearAuthRecoveryAttempts();
		expect(recordAuthRecoveryAttempt()).toBe(1);
	});

	it("is a no-op to clear when nothing was ever recorded", () => {
		expect(() => clearAuthRecoveryAttempts()).not.toThrow();
		expect(recordAuthRecoveryAttempt()).toBe(1);
	});

	it("does not throw when sessionStorage access fails", () => {
		const original = window.sessionStorage.getItem;
		window.sessionStorage.getItem = () => {
			throw new Error("storage blocked");
		};

		expect(() => recordAuthRecoveryAttempt()).not.toThrow();

		window.sessionStorage.getItem = original;
	});

	it("is exhausted only once the allowed sign-ins have been used", () => {
		expect(hasExhaustedAuthRecovery()).toBe(false);
		for (let i = 0; i < AUTH_RECOVERY_REDIRECT_LIMIT; i++)
			recordAuthRecoveryAttempt();
		expect(hasExhaustedAuthRecovery()).toBe(true);
		clearAuthRecoveryAttempts();
		expect(hasExhaustedAuthRecovery()).toBe(false);
	});
});
