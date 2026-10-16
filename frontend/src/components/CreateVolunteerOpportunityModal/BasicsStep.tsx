import { useState } from "react";
import type { ChangeEvent } from "react";
import { useTranslation } from "react-i18next";
import { Controller } from "react-hook-form";
import type { Control, UseFormRegister, UseFormWatch } from "react-hook-form";
import Dropdown from "../Dropdown";
import TagsInput from "../TagsInput";
import { FloatingField } from "./shared";
import type { OpportunityFormValues } from "./schema";
import { ChevronDownIcon, ExclamationTriangleIcon, PhotoIcon } from "../icons";
import { IMAGE_UPLOAD_ACCEPT, getImageUploadHint } from "../../lib/imageUpload";
import { inputSurfaceClass, labelClass } from "../../lib/formClasses";

const CATEGORY_VALUES = [
	"Social",
	"Environment",
	"Sport",
	"Education",
	"DisasterRelief",
	"Health",
	"Animals",
	"Culture",
	"Technology",
	"Other",
] as const;

interface Props {
	register: UseFormRegister<OpportunityFormValues>;
	watch: UseFormWatch<OpportunityFormValues>;
	control: Control<OpportunityFormValues>;
	titleDeError?: string;
	titleEnError?: string;
	descriptionDeError?: string;
	descriptionEnError?: string;
	bannerPreview: string | null;
	bannerError: string | null;
	onBannerChange: (e: ChangeEvent<HTMLInputElement>) => void;
	onBannerRemove: () => void;
}

export default function BasicsStep({
	register,
	watch,
	control,
	titleDeError,
	titleEnError,
	descriptionDeError,
	descriptionEnError,
	bannerPreview,
	bannerError,
	onBannerChange,
	onBannerRemove,
}: Props) {
	const { t, i18n } = useTranslation();
	const titleDe = watch("titleDe");
	const titleEn = watch("titleEn");
	const descriptionDe = watch("descriptionDe");
	const descriptionEn = watch("descriptionEn");
	const hasEnglishText = Boolean(titleEn.trim() || descriptionEn.trim());
	const hasEnglishError = Boolean(titleEnError || descriptionEnError);
	// Seeded from the form values rather than `false`: the step remounts on
	// every return to it, and an organizer editing an opportunity that already
	// carries a translation should land on it, not on a collapsed toggle.
	const [englishOpen, setEnglishOpen] = useState(hasEnglishText);
	// Forced open while a field inside has an error, and derived in render
	// rather than set from an effect: the wizard focuses the first invalid
	// field in the same commit the errors arrive in, and a field still
	// display:none at that point cannot take focus.
	const englishExpanded = englishOpen || hasEnglishError;
	const hasGermanError = Boolean(titleDeError || descriptionDeError);
	const [germanOpen, setGermanOpen] = useState(true);
	const germanExpanded = germanOpen || hasGermanError;
	const englishStatus = hasEnglishError
		? t("createOpportunity.translationHasError")
		: hasEnglishText
			? t("createOpportunity.translationStatusDone")
			: t("createOpportunity.translationStatusEmpty");
	// Seeded the same way as `englishOpen`: an opportunity that already has a
	// banner, a category or tags opens on them.
	const [moreDetailsOpen, setMoreDetailsOpen] = useState(
		Boolean(bannerPreview || watch("category") || watch("tags").length),
	);
	const moreDetailsExpanded = moreDetailsOpen || Boolean(bannerError);
	const germanName = t("createOpportunity.languageVersionName.de");
	const englishName = t("createOpportunity.languageVersionName.en");

	return (
		<div className="space-y-4" data-testid="wizard-step-1">
			{/*
			 * Every language version of the text sits in one section, German
			 * first as the required row and each translation below it as an
			 * optional one. Every row collapses; a required row with an error
			 * is forced open, for the focus reason given above. It replaced a DE/EN toggle that
			 * carried the site language switcher's own labels and was taken for
			 * it (#2387), and that presented an optional translation as an
			 * equal of the required German text without saying why German is
			 * the one that is required.
			 */}
			<section
				aria-labelledby="opportunity-languages-heading"
				aria-describedby="opportunity-languages-hint"
				className="rounded-xl border border-gray-200"
			>
				<div className="px-4 pt-3">
					<h4
						id="opportunity-languages-heading"
						className="text-sm font-semibold text-gray-800"
					>
						{t("createOpportunity.languageVersionsHeading")}
					</h4>
					<p
						id="opportunity-languages-hint"
						className="mt-0.5 text-xs text-gray-500"
					>
						{t("createOpportunity.languageVersionsHint")}
					</p>
				</div>
				<div className="mt-2" data-testid="opportunity-language-version-de">
					<button
						type="button"
						aria-expanded={germanExpanded}
						aria-controls="opportunity-language-version-de-fields"
						data-testid="opportunity-translation-toggle-de"
						onClick={() => setGermanOpen(!germanExpanded)}
						className="flex w-full items-center justify-between gap-3 border-t border-gray-200 px-4 py-2.5 text-left text-sm font-medium text-gray-800 hover:bg-gray-50"
					>
						<span>{germanName}</span>
						<span
							className={`flex items-center gap-1 text-xs ${
								hasGermanError ? "text-red-600" : "text-brand-700"
							}`}
						>
							{hasGermanError && (
								<ExclamationTriangleIcon className="h-3 w-3" />
							)}
							{t("createOpportunity.languageVersionRequired")}
							{hasGermanError &&
								` · ${t("createOpportunity.translationHasError")}`}
							<ChevronDownIcon
								className="h-4 w-4 text-gray-500"
								open={germanExpanded}
							/>
						</span>
					</button>
					<div
						id="opportunity-language-version-de-fields"
						hidden={!germanExpanded}
						className="space-y-4 px-4 pt-1 pb-4"
					>
						<FloatingField
							id="opportunity-title"
							label={t("createOpportunity.fieldTitleInLanguage", {
								language: germanName,
							})}
							registration={register("titleDe")}
							required
							error={titleDeError}
							maxLength={150}
							showCount
							displayValue={titleDe}
						/>
						<FloatingField
							id="opportunity-description"
							label={t("createOpportunity.fieldDescriptionInLanguage", {
								language: germanName,
							})}
							registration={register("descriptionDe")}
							required
							error={descriptionDeError}
							maxLength={2000}
							multiline
							showCount
							displayValue={descriptionDe}
						/>
					</div>
				</div>
				<button
					type="button"
					aria-expanded={englishExpanded}
					aria-controls="opportunity-translation-en"
					data-testid="opportunity-translation-toggle-en"
					onClick={() => setEnglishOpen(!englishExpanded)}
					className="flex w-full items-center justify-between gap-3 border-t border-gray-200 px-4 py-2.5 text-left text-sm font-medium text-gray-800 hover:bg-gray-50"
				>
					<span>{englishName}</span>
					<span
						className={`flex items-center gap-1 text-xs ${
							hasEnglishError
								? "text-red-600"
								: hasEnglishText
									? "text-brand-700"
									: "text-gray-500"
						}`}
					>
						{hasEnglishError && <ExclamationTriangleIcon className="h-3 w-3" />}
						{t("createOpportunity.languageVersionOptional")} · {englishStatus}
						<ChevronDownIcon
							className="h-4 w-4 text-gray-500"
							open={englishExpanded}
						/>
					</span>
				</button>
				<div
					id="opportunity-translation-en"
					hidden={!englishExpanded}
					className="space-y-4 px-4 pt-1 pb-4"
				>
					<FloatingField
						id="opportunity-title-en"
						label={t("createOpportunity.fieldTitleInLanguage", {
							language: englishName,
						})}
						registration={register("titleEn")}
						error={titleEnError}
						maxLength={150}
						showCount
						displayValue={titleEn}
					/>
					<FloatingField
						id="opportunity-description-en"
						label={t("createOpportunity.fieldDescriptionInLanguage", {
							language: englishName,
						})}
						registration={register("descriptionEn")}
						error={descriptionEnError}
						maxLength={2000}
						multiline
						showCount
						displayValue={descriptionEn}
					/>
				</div>
			</section>

			{/*
			 * Everything a listing can do without sits behind one toggle, so
			 * the first step shows the required text and nothing else until
			 * the organizer asks for more (#2387).
			 */}
			<section
				aria-labelledby="opportunity-more-details-toggle"
				className="rounded-xl border border-gray-200"
			>
				<h4>
					<button
						type="button"
						id="opportunity-more-details-toggle"
						aria-expanded={moreDetailsExpanded}
						aria-controls="opportunity-more-details"
						data-testid="opportunity-more-details-toggle"
						onClick={() => setMoreDetailsOpen(!moreDetailsExpanded)}
						className="flex w-full items-center justify-between gap-3 rounded-xl px-4 py-2.5 text-left hover:bg-gray-50"
					>
						<span>
							<span className="block text-sm font-semibold text-gray-800">
								{t("createOpportunity.moreDetailsHeading")}
							</span>
							<span className="block text-xs font-normal text-gray-500">
								{t("createOpportunity.moreDetailsSummary")}
							</span>
						</span>
						<span className="flex items-center gap-1 text-xs font-normal text-gray-500">
							{t("createOpportunity.languageVersionOptional")}
							<ChevronDownIcon
								className="h-4 w-4 text-gray-500"
								open={moreDetailsExpanded}
							/>
						</span>
					</button>
				</h4>
				<div
					id="opportunity-more-details"
					hidden={!moreDetailsExpanded}
					className="space-y-4 border-t border-gray-200 px-4 pt-3 pb-4"
				>
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

					<div>
						<label htmlFor="create-category" className={`mb-1.5 ${labelClass}`}>
							{t("createOpportunity.fieldCategory")}
						</label>
						<Controller
							name="category"
							control={control}
							render={({ field }) => (
								<Dropdown
									id="create-category"
									value={field.value ?? ""}
									onChange={(v) => field.onChange(v || undefined)}
									className={inputSurfaceClass}
									options={[
										{
											value: "",
											label: t("createOpportunity.fieldCategoryNone"),
										},
										...CATEGORY_VALUES.map((c) => ({
											value: c,
											label: t(`opportunities.category.${c}`),
										})),
									]}
								/>
							)}
						/>
					</div>

					<Controller
						name="tags"
						control={control}
						render={({ field }) => (
							<TagsInput
								id="create-tags"
								label={t("createOpportunity.fieldTags")}
								value={field.value}
								onChange={field.onChange}
								placeholder={t("createOpportunity.fieldTagsPlaceholder")}
								hint={t("createOpportunity.fieldTagsHint")}
							/>
						)}
					/>
				</div>
			</section>
		</div>
	);
}
