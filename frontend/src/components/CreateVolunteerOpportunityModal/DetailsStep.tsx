import { useEffect, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Controller } from "react-hook-form";
import type { Control } from "react-hook-form";
import Dropdown from "../Dropdown";
import TagsInput from "../TagsInput";
import DatePicker from "../DatePicker";
import ErrorBanner from "../ErrorBanner";
import Chip from "../Chip";
import Button from "../Button";
import { PlusIcon } from "../icons";
import { formatDateTimeRange } from "../../lib/format";
import { inputSurfaceClass, labelClass } from "../../lib/formClasses";
import type { OpportunityFormValues } from "./schema";
import { findOverlappingSlotIds } from "./timeSlots";

/** Where focus lands when the slot editor closes after adding a slot. */
export const ADD_SLOT_BUTTON_ID = "add-time-slot";

/** Where focus lands when the slot editor closes after editing this slot. */
export function editSlotButtonId(slotId: string): string {
	return `edit-slot-${slotId}`;
}

export type SeriesEditScope = "Only" | "ThisAndFollowing" | "EntireSeries";

export interface TimeSlotRow {
	id: string;
	startDateTime: string;
	endDateTime: string;
	maxParticipants: number | null;
	bookedCount: number;
	persisted: boolean;

	seriesId?: string;
	recurrenceFrequency?: string;
	recurrenceCount?: number;

	seriesPosition?: number;
}

interface Props {
	control: Control<OpportunityFormValues>;
	isScheduledSlots: boolean;
	allTimeSlots: TimeSlotRow[];
	removingSlotId: string | null;
	onRemoveExistingSlot: (id: string, bookedCount: number) => void;
	onRemovePendingSlot: (id: string) => void;
	onRequestRemoveSeriesSlot: (id: string, bookedCount: number) => void;
	onStartEditSlot: (slot: TimeSlotRow) => void;
	onOpenAddSlot: () => void;
	slotError: string | null;

	/**
	 * True while editing an opportunity that already exists, where adding,
	 * editing or removing a slot writes straight to the server rather than
	 * being staged for the wizard's own Save button (#2315).
	 */
	slotChangesAreImmediate: boolean;
	error: string | null;

	errorToken: number;
}

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

export default function DetailsStep({
	control,
	isScheduledSlots,
	allTimeSlots,
	removingSlotId,
	onRemoveExistingSlot,
	onRemovePendingSlot,
	onRequestRemoveSeriesSlot,
	onStartEditSlot,
	onOpenAddSlot,
	slotError,
	slotChangesAreImmediate,
	error,
	errorToken,
}: Props) {
	const { t, i18n } = useTranslation();
	const errorRef = useRef<HTMLParagraphElement>(null);

	const today = new Date();
	const todayDateInputValue = new Date(
		today.getTime() - today.getTimezoneOffset() * 60000,
	)
		.toISOString()
		.slice(0, 10);

	useEffect(() => {
		if (!error) return;
		errorRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
		errorRef.current?.focus();
	}, [error, errorToken]);

	// Two slots covering the same hour let one volunteer sign up for both and
	// be double-booked, so the list flags them; the slot editor warns before
	// one more is added (#2325).
	const overlappingSlotIds = useMemo(
		() => findOverlappingSlotIds(allTimeSlots),
		[allTimeSlots],
	);

	function capacityLabel(maxParticipants: number | null): string {
		return maxParticipants === null
			? t("timeSlots.capacityUnlimited")
			: t("timeSlots.capacity", { count: maxParticipants });
	}

	return (
		<div className="space-y-5" data-testid="wizard-step-4">
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

			{isScheduledSlots && (
				<div className="rounded-card border border-gray-200 bg-gray-50 p-4">
					<p className="mb-1 text-sm font-semibold text-gray-800">
						{t("timeSlots.sectionTitle")}
					</p>

					{slotChangesAreImmediate && (
						<p className="mb-3 text-xs text-amber-700">
							{t("timeSlots.immediateHint")}
						</p>
					)}

					{allTimeSlots.length === 0 ? (
						<p className="mb-3 text-xs text-gray-500">
							{t("timeSlots.noSlots")}
						</p>
					) : (
						<ul className="mb-3 space-y-2" data-testid="time-slot-list">
							{allTimeSlots.map((slot) => (
								<li
									key={slot.id}
									className="flex flex-col gap-1 rounded-lg bg-white px-3 py-2 text-sm shadow-sm sm:flex-row sm:items-center sm:justify-between"
								>
									<span className="text-gray-700">
										{formatDateTimeRange(
											slot.startDateTime,
											slot.endDateTime,
											i18n.language,
										)}
										<span className="text-gray-500">
											{" · "}
											{capacityLabel(slot.maxParticipants)}
										</span>
										{overlappingSlotIds.has(slot.id) && (
											<Chip tone="warning" size="sm" className="ml-2">
												{t("timeSlots.overlapWarning")}
											</Chip>
										)}
										{slot.seriesId && slot.recurrenceCount && (
											<Chip tone="brand" size="sm" className="ml-2">
												{t("timeSlots.seriesBadge", {
													frequency: t(
														`timeSlots.recurrence${slot.recurrenceFrequency}`,
													),
													position: slot.seriesPosition,
													count: slot.recurrenceCount,
												})}
											</Chip>
										)}
										{slot.bookedCount > 0 && (
											<span className="ml-2 text-xs text-gray-500">
												{t("timeSlots.bookedCount", {
													count: slot.bookedCount,
												})}
											</span>
										)}
									</span>
									<span className="flex shrink-0 items-center gap-1 sm:ml-2">
										{slot.persisted && (
											<button
												type="button"
												id={editSlotButtonId(slot.id)}
												onClick={() => onStartEditSlot(slot)}
												className="min-h-6 rounded px-2 text-xs text-brand-700 hover:underline"
											>
												{t("timeSlots.editButton")}
											</button>
										)}
										<button
											type="button"
											disabled={slot.persisted && removingSlotId === slot.id}
											onClick={() => {
												if (slot.persisted && slot.seriesId) {
													onRequestRemoveSeriesSlot(slot.id, slot.bookedCount);
												} else if (slot.persisted) {
													onRemoveExistingSlot(slot.id, slot.bookedCount);
												} else {
													onRemovePendingSlot(slot.id);
												}
											}}
											className="min-h-6 rounded px-2 text-xs text-red-600 hover:underline disabled:opacity-50"
										>
											{slot.persisted && removingSlotId === slot.id
												? t("timeSlots.removing")
												: t("timeSlots.removeButton")}
										</button>
									</span>
								</li>
							))}
						</ul>
					)}

					{slotError && (
						<p className="mb-3 text-xs text-red-600" role="alert">
							{slotError}
						</p>
					)}

					<Button
						type="button"
						id={ADD_SLOT_BUTTON_ID}
						variant="brandOutline"
						fullWidth
						onClick={onOpenAddSlot}
					>
						<PlusIcon className="h-4 w-4" />
						{t("timeSlots.addTitle")}
					</Button>
				</div>
			)}

			{!isScheduledSlots && (
				<div className="rounded-card border border-gray-200 bg-gray-50 p-4">
					<label
						htmlFor="create-valid-until"
						className={`mb-1.5 ${labelClass}`}
					>
						{t("createOpportunity.fieldValidUntil")}
					</label>
					<Controller
						name="validUntil"
						control={control}
						render={({ field }) => (
							<DatePicker
								id="create-valid-until"
								value={field.value}
								min={todayDateInputValue}
								onChange={field.onChange}
							/>
						)}
					/>
					<p className="mt-1.5 text-xs text-gray-500">
						{t("createOpportunity.validUntilHint")}
					</p>
				</div>
			)}

			{error && (
				<ErrorBanner
					ref={errorRef}
					message={error}
					tabIndex={-1}
					className="focus:outline-none"
				/>
			)}
		</div>
	);
}
