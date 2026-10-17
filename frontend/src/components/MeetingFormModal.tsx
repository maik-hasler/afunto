import { useState } from "react";
import { useTranslation } from "react-i18next";
import { getApiErrorMessage } from "../lib/apiError";
import {
	inputClass,
	inputSurfaceClass,
	labelClass,
	textareaClass,
} from "../lib/formClasses";
import {
	meetingLink,
	parseMeetingTimes,
	suggestEndTime,
	type MeetingDraft,
	type MeetingFormat,
	type MeetingTimesError,
} from "../lib/meetings";
import {
	CANONICAL_TIME_ZONE,
	toZonedDatetimeLocalValue,
} from "../lib/timezone";
import Button from "./Button";
import CharCount from "./CharCount";
import DatePicker from "./DatePicker";
import ErrorBanner from "./ErrorBanner";
import { GlobeIcon, MapPinIcon } from "./icons";
import Modal from "./Modal";
import { RequiredFieldsLegend, RequiredMark } from "./RequiredMark";

// Mirror Meeting.MaxLocationLength / MaxNoteLength (Domain/Engagements/Meeting.cs).
const MAX_LOCATION_LENGTH = 300;
const MAX_NOTE_LENGTH = 500;

const FORMATS: readonly MeetingFormat[] = ["InPerson", "Online"];

export type MeetingFormValues = MeetingDraft;

/** "yyyy-MM-dd" and "HH:mm" of `date` as wall-clock time in Berlin. */
function splitZoned(date: Date | undefined): { day: string; time: string } {
	if (!date) return { day: "", time: "" };
	const [day, time] = toZonedDatetimeLocalValue(
		date,
		CANONICAL_TIME_ZONE,
	).split("T");
	return { day, time };
}

interface Props {
	title: string;
	/** One sentence on what sending does and who hears about it. */
	intro: string;
	/**
	 * Prefills the form: the whole meeting when moving it, only how and where
	 * when answering with another time - a counter-proposal usually keeps the
	 * place and changes the time.
	 */
	initial?: Partial<MeetingFormValues>;
	onSubmit: (values: MeetingFormValues) => Promise<void>;
	onClose: () => void;
}

/**
 * Proposing, moving and answering with another time are the same form for
 * both sides, so the organizer's and the volunteer's version cannot drift
 * apart. A proposal is never edited in place - whatever this sends replaces
 * the current one.
 *
 * A meeting is a conversation, not a shift: it happens on one day, between two
 * times, so the form asks for exactly that instead of two full date-times. How
 * the two sides meet is an explicit choice, and each choice gets the field it
 * needs - an address for a meeting in person, a link for one online.
 */
export default function MeetingFormModal({
	title,
	intro,
	initial,
	onSubmit,
	onClose,
}: Props) {
	const { t } = useTranslation();
	const [day, setDay] = useState(() => splitZoned(initial?.startsAt).day);
	const [startTime, setStartTime] = useState(
		() => splitZoned(initial?.startsAt).time,
	);
	const [endTime, setEndTime] = useState(
		() => splitZoned(initial?.endsAt).time,
	);
	const [format, setFormat] = useState<MeetingFormat>(
		initial?.format ?? "InPerson",
	);
	// Kept apart so switching back and forth does not lose what was typed.
	const [address, setAddress] = useState(
		initial?.format === "Online" ? "" : (initial?.location ?? ""),
	);
	const [link, setLink] = useState(
		initial?.format === "Online" ? (initial.location ?? "") : "",
	);
	const [note, setNote] = useState(initial?.note ?? "");
	const [timesError, setTimesError] = useState<MeetingTimesError | null>(null);
	const [linkInvalid, setLinkInvalid] = useState(false);
	const [submitting, setSubmitting] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const today = splitZoned(new Date()).day;

	const location = format === "Online" ? link : address;

	function handleStartTimeChange(value: string) {
		setStartTime(value);
		setTimesError(null);
		// Most meetings last an hour; filling the end in saves a second pick
		// without hiding it. An end the user already set later than the new
		// start is theirs and stays.
		if (!endTime || endTime <= value) {
			const suggested = suggestEndTime(value);
			if (suggested) setEndTime(suggested);
		}
	}

	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		const times = parseMeetingTimes(day, startTime, endTime);
		if (!times.ok) {
			setTimesError(times.error);
			return;
		}
		if (format === "Online" && !meetingLink(link)) {
			setLinkInvalid(true);
			return;
		}
		setSubmitting(true);
		setError(null);
		try {
			await onSubmit({
				startsAt: times.startsAt,
				endsAt: times.endsAt,
				format,
				location: location.trim(),
				note: note.trim() || null,
			});
			onClose();
		} catch (err) {
			setError(getApiErrorMessage(err, t("meetings.form.submitError")));
		} finally {
			setSubmitting(false);
		}
	}

	const timesDescribedBy = timesError ? "meeting-times-error" : undefined;

	return (
		<Modal
			onClose={onClose}
			labelledBy="meeting-form-title"
			maxWidth="max-w-lg"
			closeDisabled={submitting}
		>
			<h2
				id="meeting-form-title"
				className="mb-1 text-lg font-semibold text-gray-900"
			>
				{title}
			</h2>
			<p className="mb-5 text-sm text-gray-600">{intro}</p>

			{/* noValidate: the times and the link are checked here, with messages */}
			{/* that say what is wrong, instead of the browser's own bubbles. */}
			<form
				noValidate
				onSubmit={(e) => void handleSubmit(e)}
				className="space-y-5"
			>
				<RequiredFieldsLegend />

				<fieldset className="space-y-3">
					<legend className="sr-only">{t("meetings.form.whenLegend")}</legend>
					<div>
						<label htmlFor="meeting-day" className={labelClass}>
							{t("meetings.form.dayLabel")}
							<RequiredMark />
						</label>
						<DatePicker
							id="meeting-day"
							value={day}
							min={today}
							onChange={(value) => {
								setDay(value);
								setTimesError(null);
							}}
							aria-invalid={timesError !== null}
							aria-describedby={timesDescribedBy}
						/>
					</div>
					<div className="grid grid-cols-2 gap-3">
						<div>
							<label htmlFor="meeting-start-time" className={labelClass}>
								{t("meetings.form.startTimeLabel")}
								<RequiredMark />
							</label>
							<input
								id="meeting-start-time"
								type="time"
								required
								value={startTime}
								onChange={(e) => handleStartTimeChange(e.target.value)}
								aria-invalid={timesError !== null || undefined}
								aria-describedby={timesDescribedBy}
								className={inputSurfaceClass}
							/>
						</div>
						<div>
							<label htmlFor="meeting-end-time" className={labelClass}>
								{t("meetings.form.endTimeLabel")}
								<RequiredMark />
							</label>
							<input
								id="meeting-end-time"
								type="time"
								required
								value={endTime}
								onChange={(e) => {
									setEndTime(e.target.value);
									setTimesError(null);
								}}
								aria-invalid={timesError !== null || undefined}
								aria-describedby={timesDescribedBy}
								className={inputSurfaceClass}
							/>
						</div>
					</div>
					{timesError && (
						<p
							id="meeting-times-error"
							role="alert"
							className="text-xs text-red-700"
						>
							{t(`meetings.form.timesError.${timesError}`)}
						</p>
					)}
				</fieldset>

				<fieldset>
					<legend className={labelClass}>
						{t("meetings.form.formatLegend")}
						<RequiredMark />
					</legend>
					<div className="grid grid-cols-2 gap-3">
						{FORMATS.map((value) => {
							const Icon = value === "Online" ? GlobeIcon : MapPinIcon;
							const selected = format === value;
							return (
								<label
									key={value}
									className={`flex cursor-pointer items-center justify-center gap-2 rounded-xl border-2 px-3 py-2.5 text-sm font-medium transition ${
										selected
											? "border-brand-600 bg-brand-50 text-brand-800"
											: "border-gray-200 bg-white text-gray-700 hover:border-brand-200 hover:bg-gray-50"
									}`}
								>
									<input
										type="radio"
										name="meeting-format"
										value={value}
										checked={selected}
										onChange={() => {
											setFormat(value);
											setLinkInvalid(false);
										}}
										className="sr-only"
									/>
									<Icon className="h-4 w-4 shrink-0" />
									{t(`meetings.form.format.${value}`)}
								</label>
							);
						})}
					</div>
				</fieldset>

				{format === "InPerson" ? (
					<div>
						<label htmlFor="meeting-address" className={labelClass}>
							{t("meetings.form.addressLabel")}
							<RequiredMark />
						</label>
						<input
							id="meeting-address"
							type="text"
							required
							autoComplete="street-address"
							maxLength={MAX_LOCATION_LENGTH}
							value={address}
							onChange={(e) => setAddress(e.target.value)}
							placeholder={t("meetings.form.addressPlaceholder")}
							className={inputClass}
						/>
					</div>
				) : (
					<div>
						<label htmlFor="meeting-link" className={labelClass}>
							{t("meetings.form.linkLabel")}
							<RequiredMark />
						</label>
						<input
							id="meeting-link"
							type="url"
							inputMode="url"
							required
							maxLength={MAX_LOCATION_LENGTH}
							value={link}
							onChange={(e) => {
								setLink(e.target.value);
								setLinkInvalid(false);
							}}
							placeholder="https://"
							aria-invalid={linkInvalid || undefined}
							aria-describedby="meeting-link-hint"
							className={inputClass}
						/>
						<p
							id="meeting-link-hint"
							role={linkInvalid ? "alert" : undefined}
							className={`mt-1 text-xs ${linkInvalid ? "text-red-700" : "text-gray-500"}`}
						>
							{linkInvalid
								? t("meetings.form.linkInvalid")
								: t("meetings.form.linkHint")}
						</p>
					</div>
				)}

				<div>
					<label htmlFor="meeting-note" className={labelClass}>
						{t("meetings.form.noteLabel")}
					</label>
					<textarea
						id="meeting-note"
						rows={3}
						maxLength={MAX_NOTE_LENGTH}
						value={note}
						onChange={(e) => setNote(e.target.value)}
						placeholder={t("meetings.form.notePlaceholder")}
						className={textareaClass}
					/>
					<CharCount current={note.length} max={MAX_NOTE_LENGTH} />
				</div>

				{error && <ErrorBanner message={error} />}

				<div className="flex gap-3">
					<Button
						type="button"
						variant="secondary"
						onClick={onClose}
						disabled={submitting}
						className="flex-1"
					>
						{t("meetings.form.cancel")}
					</Button>
					<Button
						type="submit"
						disabled={submitting || location.trim() === ""}
						className="flex-1"
					>
						{submitting
							? t("meetings.form.submitting")
							: t("meetings.form.submit")}
					</Button>
				</div>
			</form>
		</Modal>
	);
}
