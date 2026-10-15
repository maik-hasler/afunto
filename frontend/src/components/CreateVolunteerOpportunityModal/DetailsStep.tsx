import { useEffect, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Controller } from "react-hook-form";
import type { Control } from "react-hook-form";
import Dropdown from "../Dropdown";
import TagsInput from "../TagsInput";
import DatePicker from "../DatePicker";
import DateTimePicker from "../DateTimePicker";
import ErrorBanner from "../ErrorBanner";
import Chip from "../Chip";
import Button from "../Button";
import { formatDateTimeRange } from "../../lib/format";
import {
	checkboxClass,
	inputSurfaceClass,
	labelClass,
	radioClass,
} from "../../lib/formClasses";
import {
	CANONICAL_TIME_ZONE,
	toZonedDatetimeLocalValue,
	zonedDatetimeLocalToUtc,
} from "../../lib/timezone";
import type { OpportunityFormValues } from "./schema";
import {
	findOverlappingSlotIds,
	hasUnaddedSlotInput,
	isCompleteDateTime,
	MAX_PARTICIPANTS_LIMIT,
	overlapsAnySlot,
} from "./timeSlots";
import type { CapacityInput, NewSlotField } from "./timeSlots";

const SLOT_ERROR_ID = "time-slot-error";
const ADD_SLOT_HINT_ID = "add-slot-datetime-hint";

function editSlotHintId(slotId: string): string {
	return `edit-slot-datetime-hint-${slotId}`;
}

function joinDescribedBy(
	...ids: (string | undefined | false)[]
): string | undefined {
	const present = ids.filter((id): id is string => Boolean(id));
	return present.length > 0 ? present.join(" ") : undefined;
}

export type SeriesEditScope = "Only" | "ThisAndFollowing" | "EntireSeries";

interface TimeSlotRow {
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

interface EditingSlot {
	id: string;
	startDateTime: string;
	endDateTime: string;
	maxParticipants: CapacityInput;
	scope: SeriesEditScope;
}

interface Props {
	control: Control<OpportunityFormValues>;
	isScheduledSlots: boolean;
	occurrence: string;
	allTimeSlots: TimeSlotRow[];
	removingSlotId: string | null;
	onRemoveExistingSlot: (id: string, bookedCount: number) => void;
	onRemovePendingSlot: (id: string) => void;
	onRequestRemoveSeriesSlot: (id: string, bookedCount: number) => void;
	editingSlot: EditingSlot | null;
	onStartEditSlot: (slot: TimeSlotRow) => void;
	onEditingSlotChange: (slot: EditingSlot) => void;
	onCancelEditSlot: () => void;
	onSaveEditSlot: (bookedCount: number) => void;
	updatingSlotId: string | null;
	newSlot: {
		startDateTime: string;
		endDateTime: string;
		maxParticipants: CapacityInput;
	};
	onNewSlotChange: (slot: {
		startDateTime: string;
		endDateTime: string;
		maxParticipants: CapacityInput;
	}) => void;
	slotError: string | null;
	invalidNewSlotFields: ReadonlySet<NewSlotField>;

	/**
	 * True while editing an opportunity that already exists, where adding,
	 * editing or removing a slot writes straight to the server rather than
	 * being staged for the wizard's own Save button (#2315).
	 */
	slotChangesAreImmediate: boolean;
	addingSlot: boolean;
	onAddSlot: () => void;
	onDiscardNewSlot: () => void;
	recurrenceFrequency: string;
	onRecurrenceFrequencyChange: (value: string) => void;
	recurrenceCount: number;
	onRecurrenceCountChange: (count: number) => void;
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
	occurrence,
	allTimeSlots,
	removingSlotId,
	onRemoveExistingSlot,
	onRemovePendingSlot,
	onRequestRemoveSeriesSlot,
	editingSlot,
	onStartEditSlot,
	onEditingSlotChange,
	onCancelEditSlot,
	onSaveEditSlot,
	updatingSlotId,
	newSlot,
	onNewSlotChange,
	slotError,
	invalidNewSlotFields,
	slotChangesAreImmediate,
	addingSlot,
	onAddSlot,
	onDiscardNewSlot,
	recurrenceFrequency,
	onRecurrenceFrequencyChange,
	recurrenceCount,
	onRecurrenceCountChange,
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
	// be double-booked. Neither the list nor the add form used to say so, so
	// both flag it now - the list for what is already there, the form before
	// one more is added (#2325).
	const overlappingSlotIds = useMemo(
		() => findOverlappingSlotIds(allTimeSlots),
		[allTimeSlots],
	);

	const newSlotOverlaps = useMemo(() => {
		if (
			!isCompleteDateTime(newSlot.startDateTime) ||
			!isCompleteDateTime(newSlot.endDateTime)
		)
			return false;
		return overlapsAnySlot(
			zonedDatetimeLocalToUtc(
				newSlot.startDateTime,
				CANONICAL_TIME_ZONE,
			).getTime(),
			zonedDatetimeLocalToUtc(
				newSlot.endDateTime,
				CANONICAL_TIME_ZONE,
			).getTime(),
			allTimeSlots,
		);
	}, [newSlot.startDateTime, newSlot.endDateTime, allTimeSlots]);

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
						<p className="text-xs text-gray-500">{t("timeSlots.noSlots")}</p>
					) : (
						<ul className="mb-3 space-y-2" data-testid="time-slot-list">
							{allTimeSlots.map((slot) =>
								editingSlot?.id === slot.id ? (
									<li
										key={slot.id}
										className="rounded-lg bg-white px-3 py-2 text-sm shadow-sm"
									>
										<div className="space-y-2">
											{slot.bookedCount > 0 && (
												<p className="text-xs text-amber-600">
													{t("timeSlots.bookedCount", {
														count: slot.bookedCount,
													})}
												</p>
											)}
											{slot.seriesId && (
												<fieldset>
													<legend className="mb-1 text-xs font-medium text-gray-600">
														{t("timeSlots.editScope.legend")}
													</legend>
													<div className="space-y-1">
														{(
															[
																"Only",
																"ThisAndFollowing",
																"EntireSeries",
															] as const
														).map((s) => (
															<label
																key={s}
																className="flex items-center gap-2 text-xs text-gray-700"
															>
																<input
																	type="radio"
																	name={`edit-slot-scope-${slot.id}`}
																	value={s}
																	checked={editingSlot.scope === s}
																	onChange={() =>
																		onEditingSlotChange({
																			...editingSlot,
																			scope: s,
																		})
																	}
																	className={radioClass}
																/>
																{t(`timeSlots.editScope.${s}`)}
															</label>
														))}
													</div>
												</fieldset>
											)}
											{editingSlot.scope === "Only" && (
												<div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
													<div>
														<label
															htmlFor={`edit-slot-start-${slot.id}`}
															className="mb-1 block text-xs font-medium text-gray-600"
														>
															{t("timeSlots.fieldStart")}
														</label>
														<DateTimePicker
															id={`edit-slot-start-${slot.id}`}
															label={t("timeSlots.fieldStart")}
															value={editingSlot.startDateTime}
															onChange={(value) =>
																onEditingSlotChange({
																	...editingSlot,
																	startDateTime: value,
																})
															}
															aria-describedby={editSlotHintId(slot.id)}
														/>
													</div>
													<div>
														<label
															htmlFor={`edit-slot-end-${slot.id}`}
															className="mb-1 block text-xs font-medium text-gray-600"
														>
															{t("timeSlots.fieldEnd")}
														</label>
														<DateTimePicker
															id={`edit-slot-end-${slot.id}`}
															label={t("timeSlots.fieldEnd")}
															value={editingSlot.endDateTime}
															onChange={(value) =>
																onEditingSlotChange({
																	...editingSlot,
																	endDateTime: value,
																})
															}
															aria-describedby={editSlotHintId(slot.id)}
														/>
													</div>
												</div>
											)}
											{editingSlot.scope === "Only" && (
												<p
													id={editSlotHintId(slot.id)}
													className="text-xs text-gray-500"
												>
													{t("timeSlots.dateTimeHint")}
												</p>
											)}
											<div>
												<label
													htmlFor={`edit-slot-max-${slot.id}`}
													className="mb-1 block text-xs font-medium text-gray-600"
												>
													{t("timeSlots.fieldMaxParticipants")}
												</label>
												<div className="flex items-center gap-3">
													<input
														id={`edit-slot-max-${slot.id}`}
														type="number"
														min={1}
														max={MAX_PARTICIPANTS_LIMIT}
														disabled={editingSlot.maxParticipants === null}
														value={editingSlot.maxParticipants ?? ""}
														onChange={(e) =>
															onEditingSlotChange({
																...editingSlot,
																maxParticipants: e.target.value,
															})
														}
														className="w-24 rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm shadow-sm transition focus:border-brand-400 disabled:bg-gray-100 disabled:text-gray-400"
													/>
													<label
														htmlFor={`edit-slot-unlimited-${slot.id}`}
														className="flex items-center gap-1.5 text-xs font-medium text-gray-600"
													>
														<input
															id={`edit-slot-unlimited-${slot.id}`}
															type="checkbox"
															checked={editingSlot.maxParticipants === null}
															onChange={(e) =>
																onEditingSlotChange({
																	...editingSlot,
																	maxParticipants: e.target.checked
																		? null
																		: "1",
																})
															}
															className={`h-3.5 w-3.5 ${checkboxClass}`}
														/>
														{t("timeSlots.unlimited")}
													</label>
												</div>
											</div>
											<div className="flex justify-end gap-3">
												<button
													type="button"
													onClick={onCancelEditSlot}
													className="text-xs text-gray-500 hover:underline"
												>
													{t("timeSlots.cancelEditButton")}
												</button>
												<Button
													type="button"
													variant="brandOutline"
													size="sm"
													disabled={
														updatingSlotId === slot.id ||
														(editingSlot.scope === "Only" &&
															(!isCompleteDateTime(editingSlot.startDateTime) ||
																!isCompleteDateTime(editingSlot.endDateTime)))
													}
													onClick={() => onSaveEditSlot(slot.bookedCount)}
												>
													{updatingSlotId === slot.id
														? t("timeSlots.editing")
														: t("timeSlots.saveButton")}
												</Button>
											</div>
										</div>
									</li>
								) : (
									<li
										key={slot.id}
										className="flex items-center justify-between rounded-lg bg-white px-3 py-2 text-sm shadow-sm"
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
										<span className="ml-2 flex shrink-0 items-center gap-3">
											{slot.persisted && (
												<button
													type="button"
													onClick={() => onStartEditSlot(slot)}
													className="text-xs text-brand-700 hover:underline"
												>
													{t("timeSlots.editButton")}
												</button>
											)}
											<button
												type="button"
												disabled={slot.persisted && removingSlotId === slot.id}
												onClick={() => {
													if (slot.persisted && slot.seriesId) {
														onRequestRemoveSeriesSlot(
															slot.id,
															slot.bookedCount,
														);
													} else if (slot.persisted) {
														onRemoveExistingSlot(slot.id, slot.bookedCount);
													} else {
														onRemovePendingSlot(slot.id);
													}
												}}
												className="text-xs text-red-600 hover:underline disabled:opacity-50"
											>
												{slot.persisted && removingSlotId === slot.id
													? t("timeSlots.removing")
													: t("timeSlots.removeButton")}
											</button>
										</span>
									</li>
								),
							)}
						</ul>
					)}

					<div className="space-y-3 border-t border-gray-200 pt-3">
						<p className="text-xs font-semibold text-gray-700">
							{t("timeSlots.addTitle")}
						</p>
						<div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
							<div>
								<label
									htmlFor="slot-start"
									className="mb-1 block text-xs font-medium text-gray-600"
								>
									{t("timeSlots.fieldStart")}
								</label>
								<DateTimePicker
									id="slot-start"
									label={t("timeSlots.fieldStart")}
									value={newSlot.startDateTime}
									min={toZonedDatetimeLocalValue(
										new Date(),
										CANONICAL_TIME_ZONE,
									)}
									onChange={(value) =>
										onNewSlotChange({
											...newSlot,
											startDateTime: value,
										})
									}
									aria-invalid={invalidNewSlotFields.has("start")}
									aria-describedby={joinDescribedBy(
										ADD_SLOT_HINT_ID,
										invalidNewSlotFields.has("start") && SLOT_ERROR_ID,
									)}
								/>
							</div>
							<div>
								<label
									htmlFor="slot-end"
									className="mb-1 block text-xs font-medium text-gray-600"
								>
									{t("timeSlots.fieldEnd")}
								</label>
								<DateTimePicker
									id="slot-end"
									label={t("timeSlots.fieldEnd")}
									value={newSlot.endDateTime}
									onChange={(value) =>
										onNewSlotChange({
											...newSlot,
											endDateTime: value,
										})
									}
									aria-invalid={invalidNewSlotFields.has("end")}
									aria-describedby={joinDescribedBy(
										ADD_SLOT_HINT_ID,
										invalidNewSlotFields.has("end") && SLOT_ERROR_ID,
									)}
								/>
							</div>
						</div>
						<p id={ADD_SLOT_HINT_ID} className="text-xs text-gray-500">
							{t("timeSlots.dateTimeHint")}
						</p>
						<div>
							<label
								htmlFor="slot-max"
								className="mb-1 block text-xs font-medium text-gray-600"
							>
								{t("timeSlots.fieldMaxParticipants")}
							</label>
							<div className="flex items-center gap-3">
								<input
									id="slot-max"
									type="number"
									min={1}
									max={MAX_PARTICIPANTS_LIMIT}
									disabled={newSlot.maxParticipants === null}
									aria-invalid={invalidNewSlotFields.has("max") || undefined}
									aria-describedby={
										invalidNewSlotFields.has("max") ? SLOT_ERROR_ID : undefined
									}
									value={newSlot.maxParticipants ?? ""}
									onChange={(e) =>
										onNewSlotChange({
											...newSlot,
											maxParticipants: e.target.value,
										})
									}
									className="w-24 rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm shadow-sm transition focus:border-brand-400 disabled:bg-gray-100 disabled:text-gray-400"
								/>
								<label
									htmlFor="slot-unlimited"
									className="flex items-center gap-1.5 text-xs font-medium text-gray-600"
								>
									<input
										id="slot-unlimited"
										type="checkbox"
										checked={newSlot.maxParticipants === null}
										onChange={(e) =>
											onNewSlotChange({
												...newSlot,
												maxParticipants: e.target.checked ? null : "1",
											})
										}
										className={`h-3.5 w-3.5 ${checkboxClass}`}
									/>
									{t("timeSlots.unlimited")}
								</label>
							</div>
						</div>
						{occurrence === "Recurring" && (
							<div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
								<div>
									<label
										htmlFor="slot-recurrence-frequency"
										className="mb-1 block text-xs font-medium text-gray-600"
									>
										{t("timeSlots.recurrenceFrequency")}
									</label>
									<Dropdown
										id="slot-recurrence-frequency"
										value={recurrenceFrequency}
										onChange={onRecurrenceFrequencyChange}
										className={inputSurfaceClass}
										options={[
											{
												value: "Weekly",
												label: t("timeSlots.recurrenceWeekly"),
											},
											{
												value: "Monthly",
												label: t("timeSlots.recurrenceMonthly"),
											},
										]}
									/>
								</div>
								<div>
									<label
										htmlFor="slot-recurrence-count"
										className="mb-1 block text-xs font-medium text-gray-600"
									>
										{t("timeSlots.recurrenceCount")}
									</label>
									<input
										id="slot-recurrence-count"
										type="number"
										min={1}
										max={52}
										value={recurrenceCount}
										onChange={(e) =>
											onRecurrenceCountChange(
												Math.max(
													1,
													Math.min(52, parseInt(e.target.value, 10) || 1),
												),
											)
										}
										className={inputSurfaceClass}
									/>
								</div>
							</div>
						)}
						{newSlotOverlaps && (
							<p className="text-xs text-amber-700" role="status">
								{t("timeSlots.overlapHint")}
							</p>
						)}
						{hasUnaddedSlotInput(newSlot) && (
							<p
								className="text-xs font-medium text-amber-700"
								role="status"
								data-testid="unadded-slot-hint"
							>
								{t("timeSlots.unaddedHint")}{" "}
								<button
									type="button"
									onClick={onDiscardNewSlot}
									className="font-normal text-gray-600 underline hover:text-gray-800"
								>
									{t("timeSlots.discardNewSlot")}
								</button>
							</p>
						)}
						{slotError && (
							<p
								id={SLOT_ERROR_ID}
								className="text-xs text-red-600"
								role="alert"
							>
								{slotError}
							</p>
						)}
						<Button
							type="button"
							variant="brandOutline"
							size="sm"
							disabled={
								addingSlot ||
								!isCompleteDateTime(newSlot.startDateTime) ||
								!isCompleteDateTime(newSlot.endDateTime)
							}
							onClick={onAddSlot}
						>
							{addingSlot ? t("timeSlots.adding") : t("timeSlots.addButton")}
						</Button>
					</div>
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
