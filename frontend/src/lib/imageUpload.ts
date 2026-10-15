import type { TFunction } from "i18next";
import { resolveDateLocale } from "./format";

export const IMAGE_UPLOAD_TYPES: readonly string[] = [
	"image/jpeg",
	"image/png",
	"image/webp",
];
// Caps the file the user picks, not what gets uploaded: ImageCropModal re-encodes
// every image to a small fixed size, so the backend's 2 MB limit applies to the
// cropped output. This cap only keeps the browser from decoding absurdly large
// files; typical phone photos (3-15 MB) must pass it.
export const MAX_IMAGE_SOURCE_BYTES = 20 * 1024 * 1024;

export const IMAGE_UPLOAD_ACCEPT = IMAGE_UPLOAD_TYPES.join(",");

const KILOBYTE = 1024;
const MEGABYTE = 1024 * 1024;

const MAX_FILE_NAME_CHARS = 40;

const numberFormatters = new Map<string, Intl.NumberFormat>();

function getNumberFormatter(
	locale: string,
	maximumFractionDigits: number,
): Intl.NumberFormat {
	const cacheKey = `${locale}|${maximumFractionDigits}`;
	let formatter = numberFormatters.get(cacheKey);
	if (!formatter) {
		formatter = new Intl.NumberFormat(locale, { maximumFractionDigits });
		numberFormatters.set(cacheKey, formatter);
	}
	return formatter;
}

export function formatFileSize(bytes: number, lng: string): string {
	const locale = resolveDateLocale(lng);
	if (bytes >= MEGABYTE)
		return `${getNumberFormatter(locale, 1).format(bytes / MEGABYTE)} MB`;
	if (bytes >= KILOBYTE)
		return `${getNumberFormatter(locale, 0).format(bytes / KILOBYTE)} KB`;
	return `${getNumberFormatter(locale, 0).format(bytes)} B`;
}

export function truncateFileName(fileName: string): string {
	if (fileName.length <= MAX_FILE_NAME_CHARS) return fileName;

	const dotIndex = fileName.lastIndexOf(".");
	const extension = dotIndex > 0 ? fileName.slice(dotIndex) : "";

	if (extension.length > 10)
		return `${fileName.slice(0, MAX_FILE_NAME_CHARS - 1)}…`;

	const head = fileName.slice(0, MAX_FILE_NAME_CHARS - extension.length - 1);
	return `${head}…${extension}`;
}

export function getImageUploadHint(t: TFunction, lng: string): string {
	return t("imageUpload.hint", {
		max: formatFileSize(MAX_IMAGE_SOURCE_BYTES, lng),
	});
}

export function validateImageUpload(
	file: File,
	t: TFunction,
	lng: string,
): string | null {
	const fileName = truncateFileName(file.name);

	if (!IMAGE_UPLOAD_TYPES.includes(file.type))
		return t("imageUpload.wrongType", { fileName });

	if (file.size > MAX_IMAGE_SOURCE_BYTES)
		return t("imageUpload.tooLarge", {
			fileName,
			size: formatFileSize(file.size, lng),
			max: formatFileSize(MAX_IMAGE_SOURCE_BYTES, lng),
		});

	return null;
}
