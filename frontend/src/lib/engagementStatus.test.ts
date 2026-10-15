import { describe, expect, it } from "vitest";
import {
	activeEngagementStatusByOpportunity,
	isTerminalEngagementStatus,
} from "./engagementStatus";

describe("isTerminalEngagementStatus", () => {
	it("returns true for Cancelled and Withdrawn", () => {
		expect(isTerminalEngagementStatus("Cancelled")).toBe(true);
		expect(isTerminalEngagementStatus("Withdrawn")).toBe(true);
	});

	it("returns false for Pending and Confirmed", () => {
		expect(isTerminalEngagementStatus("Pending")).toBe(false);
		expect(isTerminalEngagementStatus("Confirmed")).toBe(false);
	});
});

describe("activeEngagementStatusByOpportunity", () => {
	it("maps each opportunity to its engagement's status", () => {
		const result = activeEngagementStatusByOpportunity([
			{ opportunityId: "a", status: "Pending" },
			{ opportunityId: "b", status: "Confirmed" },
		]);

		expect(result).toEqual(
			new Map([
				["a", "Pending"],
				["b", "Confirmed"],
			]),
		);
	});

	it("drops withdrawn and cancelled engagements", () => {
		const result = activeEngagementStatusByOpportunity([
			{ opportunityId: "a", status: "Withdrawn" },
			{ opportunityId: "b", status: "Cancelled" },
		]);

		expect(result.size).toBe(0);
	});

	it("lets a confirmed engagement outrank a pending one on the same opportunity, in either order", () => {
		const confirmedFirst = activeEngagementStatusByOpportunity([
			{ opportunityId: "a", status: "Confirmed" },
			{ opportunityId: "a", status: "Pending" },
		]);
		const pendingFirst = activeEngagementStatusByOpportunity([
			{ opportunityId: "a", status: "Pending" },
			{ opportunityId: "a", status: "Confirmed" },
		]);

		expect(confirmedFirst.get("a")).toBe("Confirmed");
		expect(pendingFirst.get("a")).toBe("Confirmed");
	});

	it("keeps an active status when a terminal engagement on the same opportunity follows it", () => {
		const result = activeEngagementStatusByOpportunity([
			{ opportunityId: "a", status: "Pending" },
			{ opportunityId: "a", status: "Withdrawn" },
		]);

		expect(result.get("a")).toBe("Pending");
	});
});
