import { z } from "zod/mini";
import type { TFunction } from "i18next";

export const TOTAL_STEPS = 3;

const OCCURRENCE_VALUES = ["OneTime", "Recurring"] as const;
const PARTICIPATION_TYPE_VALUES = [
	"ScheduledSlots",
	"IndividualContact",
] as const;
const CHECK_IN_METHOD_VALUES = ["None", "QRCode", "PINCode", "Manual"] as const;

export function buildOpportunityFormSchema(t: TFunction) {
	const titleRequired = t("createOpportunity.titleRequired");
	const descriptionRequired = t("createOpportunity.descriptionRequired");
	const translationPairRequired = t(
		"createOpportunity.translationPairRequired",
	);
	const streetRequired = t("createOpportunity.streetRequired");
	const houseNumberRequired = t("createOpportunity.houseNumberRequired");
	const zipRequired = t("createOpportunity.zipRequired");
	const cityRequired = t("createOpportunity.cityRequired");
	const invalidPin = t("createOpportunity.checkInPinInvalid");
	const tooLong = (max: number) => t("createOpportunity.fieldTooLong", { max });

	return z
		.object({
			titleDe: z.string().check(z.maxLength(150, tooLong(150))),
			titleEn: z.string().check(z.maxLength(150, tooLong(150))),
			descriptionDe: z.string().check(z.maxLength(2000, tooLong(2000))),
			descriptionEn: z.string().check(z.maxLength(2000, tooLong(2000))),
			isRemote: z.boolean(),
			street: z.string().check(z.maxLength(100, tooLong(100))),
			houseNumber: z.string().check(z.maxLength(10, tooLong(10))),
			zipCode: z.string().check(z.maxLength(5, tooLong(5))),
			city: z.string().check(z.maxLength(100, tooLong(100))),
			occurrence: z.enum(OCCURRENCE_VALUES),
			participationType: z.enum(PARTICIPATION_TYPE_VALUES),
			checkInMethod: z.enum(CHECK_IN_METHOD_VALUES),
			checkInPin: z.string(),
			category: z.optional(z.string()),
			tags: z.array(z.string()),
			validUntil: z.string(),
			membersOnly: z.boolean(),
		})
		.check(
			z.superRefine((data, ctx) => {
				if (!data.titleDe.trim())
					ctx.addIssue({
						code: "custom",
						path: ["titleDe"],
						message: titleRequired,
					});
				if (!data.descriptionDe.trim())
					ctx.addIssue({
						code: "custom",
						path: ["descriptionDe"],
						message: descriptionRequired,
					});
				// The detail page falls back to German per field, not per
				// opportunity, so an English title over a German description is
				// what a half-filled translation shows English visitors (#2387).
				const hasTitleEn = Boolean(data.titleEn.trim());
				const hasDescriptionEn = Boolean(data.descriptionEn.trim());
				if (hasTitleEn !== hasDescriptionEn)
					ctx.addIssue({
						code: "custom",
						path: [hasTitleEn ? "descriptionEn" : "titleEn"],
						message: translationPairRequired,
					});
				if (!data.isRemote) {
					if (!data.street.trim())
						ctx.addIssue({
							code: "custom",
							path: ["street"],
							message: streetRequired,
						});
					if (!data.houseNumber.trim())
						ctx.addIssue({
							code: "custom",
							path: ["houseNumber"],
							message: houseNumberRequired,
						});
					if (!data.zipCode.trim())
						ctx.addIssue({
							code: "custom",
							path: ["zipCode"],
							message: zipRequired,
						});
					if (!data.city.trim())
						ctx.addIssue({
							code: "custom",
							path: ["city"],
							message: cityRequired,
						});
				}
				if (
					data.checkInMethod === "PINCode" &&
					data.checkInPin &&
					!/^\d{6}$/.test(data.checkInPin)
				)
					ctx.addIssue({
						code: "custom",
						path: ["checkInPin"],
						message: invalidPin,
					});
			}),
		);
}

export type OpportunityFormValues = z.infer<
	ReturnType<typeof buildOpportunityFormSchema>
>;

export const STEP_FIELDS: Record<number, (keyof OpportunityFormValues)[]> = {
	1: ["titleDe", "titleEn", "descriptionDe", "descriptionEn"],
	2: ["street", "houseNumber", "zipCode", "city"],
	3: ["checkInPin"],
};

export function errorStepsFromFieldErrors(
	erroredFields: ReadonlySet<keyof OpportunityFormValues>,
): Set<number> {
	const steps = new Set<number>();
	for (const [step, fields] of Object.entries(STEP_FIELDS)) {
		if (fields.some((field) => erroredFields.has(field)))
			steps.add(Number(step));
	}
	return steps;
}
