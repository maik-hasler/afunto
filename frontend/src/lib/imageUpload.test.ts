import { describe, it, expect, vi } from "vitest";
import type { TFunction } from "i18next";
import {
	IMAGE_UPLOAD_ACCEPT,
	IMAGE_UPLOAD_TYPES,
	MAX_IMAGE_SOURCE_BYTES,
	formatFileSize,
	getImageUploadHint,
	truncateFileName,
	validateImageUpload,
} from "./imageUpload";

function fakeT(): TFunction {
	return vi.fn((key: string, options?: Record<string, unknown>) =>
		options ? `${key}:${JSON.stringify(options)}` : key,
	) as unknown as TFunction;
}

function fakeFile(name: string, type: string, size: number): File {
	return { name, type, size } as File;
}

describe("formatFileSize", () => {
	it("formats megabytes with the German decimal comma", () => {
		expect(formatFileSize(4.2 * 1024 * 1024, "de")).toBe("4,2 MB");
	});

	it("formats megabytes with the English decimal point", () => {
		expect(formatFileSize(4.2 * 1024 * 1024, "en")).toBe("4.2 MB");
	});

	it("drops a trailing zero fraction", () => {
		expect(formatFileSize(MAX_IMAGE_SOURCE_BYTES, "de")).toBe("20 MB");
	});

	it("falls back to whole kilobytes below a megabyte", () => {
		expect(formatFileSize(300 * 1024, "en")).toBe("300 KB");
	});

	it("falls back to bytes below a kilobyte", () => {
		expect(formatFileSize(512, "en")).toBe("512 B");
	});
});

describe("truncateFileName", () => {
	it("leaves a normal name untouched", () => {
		expect(truncateFileName("notes.txt")).toBe("notes.txt");
	});

	it("keeps the extension when trimming a long name", () => {
		const result = truncateFileName(`${"a".repeat(80)}.txt`);
		expect(result.endsWith(".txt")).toBe(true);
		expect(result.length).toBeLessThanOrEqual(40);
	});

	it("trims the head when the extension itself is absurdly long", () => {
		const result = truncateFileName(`photo.${"z".repeat(60)}`);
		expect(result.length).toBeLessThanOrEqual(40);
		expect(result.startsWith("photo.")).toBe(true);
	});

	it("handles a long name with no extension at all", () => {
		expect(truncateFileName("b".repeat(80)).length).toBeLessThanOrEqual(40);
	});
});

describe("getImageUploadHint", () => {
	it("interpolates the ceiling instead of hardcoding it in the string", () => {
		expect(getImageUploadHint(fakeT(), "de")).toBe(
			'imageUpload.hint:{"max":"20 MB"}',
		);
	});
});

describe("validateImageUpload", () => {
	it("accepts every allowed type", () => {
		for (const type of IMAGE_UPLOAD_TYPES)
			expect(
				validateImageUpload(fakeFile("a.img", type, 1024), fakeT(), "de"),
			).toBeNull();
	});

	it("accepts a file exactly at the size ceiling", () => {
		const file = fakeFile("logo.png", "image/png", MAX_IMAGE_SOURCE_BYTES);
		expect(validateImageUpload(file, fakeT(), "de")).toBeNull();
	});

	it("accepts a typical phone photo above the 2 MB backend limit (#2404)", () => {
		const file = fakeFile("IMG_1234.jpg", "image/jpeg", 8 * 1024 * 1024);
		expect(validateImageUpload(file, fakeT(), "de")).toBeNull();
	});

	it("names the file in the wrong-type message", () => {
		const file = fakeFile("notes.txt", "text/plain", 1024);
		expect(validateImageUpload(file, fakeT(), "de")).toBe(
			'imageUpload.wrongType:{"fileName":"notes.txt"}',
		);
	});

	it("reports a file with no detected type as the wrong type", () => {
		const file = fakeFile("mystery", "", 1024);
		expect(validateImageUpload(file, fakeT(), "de")).toBe(
			'imageUpload.wrongType:{"fileName":"mystery"}',
		);
	});

	it("names the file, its size and the ceiling in the too-large message", () => {
		const file = fakeFile("huge.png", "image/png", 24.5 * 1024 * 1024);
		expect(validateImageUpload(file, fakeT(), "de")).toBe(
			'imageUpload.tooLarge:{"fileName":"huge.png","size":"24,5 MB","max":"20 MB"}',
		);
	});

	it("reports the wrong type first when a file breaks both rules", () => {
		const file = fakeFile("movie.mp4", "video/mp4", 30 * 1024 * 1024);
		expect(validateImageUpload(file, fakeT(), "de")).toBe(
			'imageUpload.wrongType:{"fileName":"movie.mp4"}',
		);
	});

	it("truncates a pathologically long name in the message", () => {
		const file = fakeFile(`${"a".repeat(80)}.txt`, "text/plain", 1024);
		const message = validateImageUpload(file, fakeT(), "de");
		expect(message).toContain("…");
		expect(message).not.toContain("a".repeat(80));
	});

	it("never answers with the hint the picker already shows", () => {
		const t = fakeT();
		const hint = getImageUploadHint(t, "de");
		const rejections = [
			validateImageUpload(fakeFile("notes.txt", "text/plain", 1024), t, "de"),
			validateImageUpload(
				fakeFile("huge.png", "image/png", 30 * 1024 * 1024),
				t,
				"de",
			),
		];

		for (const message of rejections) {
			expect(message).not.toBeNull();
			expect(message).not.toBe(hint);
			expect(message).not.toContain("imageUpload.hint");
		}
		expect(rejections[0]).not.toBe(rejections[1]);
	});
});

describe("IMAGE_UPLOAD_ACCEPT", () => {
	it("offers exactly the types the validation accepts", () => {
		expect(IMAGE_UPLOAD_ACCEPT.split(",")).toEqual(IMAGE_UPLOAD_TYPES);
	});
});
