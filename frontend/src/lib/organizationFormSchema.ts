import { z } from "zod/mini";
import type { TFunction } from "i18next";

export function buildOrganizationFormSchema(t: TFunction) {
	const nameRequired = t("orgSettings.nameRequired");
	const streetRequired = t("orgSettings.streetRequired");
	const houseNumberRequired = t("orgSettings.houseNumberRequired");
	const zipRequired = t("orgSettings.zipRequired");
	const cityRequired = t("orgSettings.cityRequired");
	const invalidZip = t("orgSettings.zipInvalid");
	const invalidWebsite = t("orgSettings.websiteInvalid");
	const tooLong = (max: number) => t("orgSettings.fieldTooLong", { max });

	return z
		.object({
			name: z.string().check(z.maxLength(100, tooLong(100))),
			description: z.string().check(z.maxLength(1000, tooLong(1000))),
			contactEmail: z.string().check(z.maxLength(254, tooLong(254))),
			contactPhone: z.string().check(z.maxLength(30, tooLong(30))),
			website: z.string().check(z.maxLength(500, tooLong(500))),
			street: z.string().check(z.maxLength(200, tooLong(200))),
			houseNumber: z.string().check(z.maxLength(20, tooLong(20))),
			zipCode: z.string().check(z.maxLength(10, tooLong(10))),
			city: z.string().check(z.maxLength(100, tooLong(100))),
		})
		.check(
			z.superRefine((data, ctx) => {
				if (!data.name.trim())
					ctx.addIssue({
						code: "custom",
						path: ["name"],
						message: nameRequired,
					});

				const website = data.website.trim();
				if (website) {
					let isValidWebsite: boolean;
					try {
						isValidWebsite = ["http:", "https:"].includes(
							new URL(website).protocol,
						);
					} catch {
						isValidWebsite = false;
					}
					if (!isValidWebsite)
						ctx.addIssue({
							code: "custom",
							path: ["website"],
							message: invalidWebsite,
						});
				}

				const hasAddress =
					data.street.trim() ||
					data.houseNumber.trim() ||
					data.zipCode.trim() ||
					data.city.trim();

				if (hasAddress) {
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
					else if (data.zipCode.trim().length !== 5)
						ctx.addIssue({
							code: "custom",
							path: ["zipCode"],
							message: invalidZip,
						});
					if (!data.city.trim())
						ctx.addIssue({
							code: "custom",
							path: ["city"],
							message: cityRequired,
						});
				}
			}),
		);
}

export type OrganizationFormValues = z.infer<
	ReturnType<typeof buildOrganizationFormSchema>
>;

export const ORGANIZATION_FORM_DEFAULT_VALUES: OrganizationFormValues = {
	name: "",
	description: "",
	contactEmail: "",
	contactPhone: "",
	website: "",
	street: "",
	houseNumber: "",
	zipCode: "",
	city: "",
};
