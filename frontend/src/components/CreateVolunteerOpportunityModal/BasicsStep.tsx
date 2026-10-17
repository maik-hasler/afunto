import { useEffect, useState } from "react";
import type { ChangeEvent } from "react";
import { useTranslation } from "react-i18next";
import type { UseFormRegister, UseFormWatch } from "react-hook-form";
import { FloatingField } from "./shared";
import type { OpportunityFormValues } from "./schema";
import { ExclamationTriangleIcon, PhotoIcon } from "../icons";
import { checkboxClass } from "../../lib/formClasses";
import { IMAGE_UPLOAD_ACCEPT, getImageUploadHint } from "../../lib/imageUpload";

interface Props {
	register: UseFormRegister<OpportunityFormValues>;
	watch: UseFormWatch<OpportunityFormValues>;
	titleDeError?: string;
	titleEnError?: string;
	descriptionDeError?: string;
	descriptionEnError?: string;

	revalidationAttempt: number;
	bannerPreview: string | null;
	bannerError: string | null;
	onBannerChange: (e: ChangeEvent<HTMLInputElement>) => void;
	onBannerRemove: () => void;
}

const CONTENT_LANGUAGES = ["de", "en"] as const;
type ContentLanguage = (typeof CONTENT_LANGUAGES)[number];

export default function BasicsStep({
	register,
	watch,
	titleDeError,
	titleEnError,
	descriptionDeError,
	descriptionEnError,
	revalidationAttempt,
	bannerPreview,
	bannerError,
	onBannerChange,
	onBannerRemove,
}: Props) {
	const { t, i18n } = useTranslation();
	const [activeLanguage, setActiveLanguage] = useState<ContentLanguage>("de");
	const titleDe = watch("titleDe");
	const titleEn = watch("titleEn");
	const descriptionDe = watch("descriptionDe");
	const descriptionEn = watch("descriptionEn");
	const hasError = {
		de: Boolean(titleDeError || descriptionDeError),
		en: Boolean(titleEnError || descriptionEnError),
	};

	useEffect(() => {
		if (titleDeError || descriptionDeError) setActiveLanguage("de");
	}, [titleDeError, descriptionDeError, revalidationAttempt]);

	return (
		<div className="space-y-4" data-testid="wizard-step-1">
			<div
				role="group"
				aria-label={t("createOpportunity.contentLanguageGroup")}
				className="inline-flex rounded-lg border border-gray-200 p-0.5"
			>
				{CONTENT_LANGUAGES.map((lang) => (
					<button
						key={lang}
						type="button"
						aria-current={activeLanguage === lang ? "true" : undefined}
						data-testid={`opportunity-content-language-${lang}`}

						data-skip-initial-focus
						onClick={() => setActiveLanguage(lang)}
						className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
							activeLanguage === lang
								? "bg-brand-50 text-brand-700"
								: "text-gray-600 hover:bg-gray-50"
						}`}
					>
						{t(`language.${lang}`)}
						{hasError[lang] && (
							<span className="inline-flex items-center">
								<ExclamationTriangleIcon className="h-3 w-3 text-red-600" />
								<span className="sr-only">
									{t("createOpportunity.contentLanguageHasError")}
								</span>
							</span>
						)}
					</button>
				))}
			</div>

			<div className={activeLanguage === "de" ? "space-y-4" : "hidden"}>
				<FloatingField
					id="opportunity-title"
					label={t("createOpportunity.fieldTitle")}
					registration={register("titleDe")}
					required
					error={titleDeError}
					maxLength={150}
					showCount
					displayValue={titleDe}
				/>
				<FloatingField
					id="opportunity-description"
					label={t("createOpportunity.fieldDescription")}
					registration={register("descriptionDe")}
					required
					error={descriptionDeError}
					maxLength={2000}
					multiline
					showCount
					displayValue={descriptionDe}
				/>
			</div>
			<div className={activeLanguage === "en" ? "space-y-4" : "hidden"}>
				<p className="text-xs text-gray-500">
					{t("createOpportunity.contentLanguageEnglishHint")}
				</p>
				<FloatingField
					id="opportunity-title-en"
					label={t("createOpportunity.fieldTitle")}
					registration={register("titleEn")}
					error={titleEnError}
					maxLength={150}
					showCount
					displayValue={titleEn}
				/>
				<FloatingField
					id="opportunity-description-en"
					label={t("createOpportunity.fieldDescription")}
					registration={register("descriptionEn")}
					error={descriptionEnError}
					maxLength={2000}
					multiline
					showCount
					displayValue={descriptionEn}
				/>
			</div>

			<div>
				<p className="mb-1.5 text-sm font-semibold text-gray-800">
					{t("createOpportunity.fieldBanner")}
				</p>
				{/*
				 * The picker stays mounted whether or not a banner is set: while
				 * one was, it used to be absent from the DOM entirely, leaving
				 * Entfernen-then-upload as the only way to swap an image (#2325).
				 */}
				<input
					id="opportunity-banner"
					type="file"
					accept={IMAGE_UPLOAD_ACCEPT}
					className="sr-only"
					onChange={onBannerChange}
					aria-invalid={bannerError ? true : undefined}
					aria-describedby={
						bannerError ? "opportunity-banner-error" : undefined
					}
				/>
				{bannerPreview ? (
					<div className="relative overflow-hidden rounded-xl">
						<img
							src={bannerPreview}
							alt={t("createOpportunity.fieldBanner")}
							width={1200}
							height={480}
							loading="lazy"
							className="h-36 w-full object-cover"
						/>
						<div className="absolute top-2 right-2 flex items-center gap-2">
							<label
								htmlFor="opportunity-banner"
								className="cursor-pointer rounded-lg bg-black/60 px-2.5 py-1 text-xs font-semibold text-white backdrop-blur transition hover:bg-black/80"
							>
								{t("createOpportunity.bannerReplace")}
							</label>
							<button
								type="button"
								onClick={onBannerRemove}
								className="rounded-lg bg-black/60 px-2.5 py-1 text-xs font-semibold text-white backdrop-blur transition hover:bg-black/80"
							>
								{t("createOpportunity.bannerRemove")}
							</button>
						</div>
					</div>
				) : (
					<label
						htmlFor="opportunity-banner"
						className="flex cursor-pointer flex-col items-center justify-center gap-1 rounded-xl border-2 border-dashed border-gray-200 bg-gray-50 px-4 py-6 text-center transition hover:border-brand-300 hover:bg-brand-50"
					>
						<PhotoIcon className="h-6 w-6 text-gray-400" />
						<span className="text-sm font-medium text-gray-700">
							{t("createOpportunity.bannerUpload")}
						</span>
						<span className="text-xs text-gray-500">
							{getImageUploadHint(t, i18n.language)}
						</span>
					</label>
				)}
				{bannerError && (
					<p
						id="opportunity-banner-error"
						className="mt-1 text-xs text-red-600"
						role="alert"
					>
						{bannerError}
					</p>
				)}
			</div>

			<label
				htmlFor="opportunity-members-only"
				className="flex cursor-pointer items-start gap-3 rounded-xl border-2 border-gray-200 bg-white px-4 py-3 transition hover:border-brand-200 hover:bg-brand-50 has-[:checked]:border-brand-500 has-[:checked]:bg-brand-50"
			>
				<input
					type="checkbox"
					id="opportunity-members-only"
					className={`mt-0.5 h-4 w-4 ${checkboxClass}`}
					{...register("membersOnly")}
				/>
				<span className="text-sm font-medium text-gray-800">
					{t("createOpportunity.fieldMembersOnly")}
					<span className="mt-0.5 block text-xs font-normal text-gray-500">
						{t("createOpportunity.fieldMembersOnlyHint")}
					</span>
				</span>
			</label>
		</div>
	);
}
