import type { ReactNode } from "react";
import type { UseFormRegisterReturn } from "react-hook-form";

import CharCount from "../CharCount";
import { fieldBorderClass } from "../../lib/formClasses";
import { RequiredMark } from "../RequiredMark";

export function Stepper({
	current,
	errorSteps,
	onStepClick,
	steps,
	stepLabel,
	blocked,
}: {
	current: number;
	errorSteps: Set<number>;
	onStepClick: (n: number) => void;
	steps: string[];
	stepLabel: (n: number, label: string) => string;

	blocked?: { step: number; messageId: string };
}) {
	return (
		<ol className="mt-4 flex items-stretch gap-1.5">
			{steps.map((label, i) => {
				const n = i + 1;
				const isActive = n === current;
				const hasError = errorSteps.has(n);
				const isDone = n < current;
				return (
					<li key={label} className="min-w-0 flex-1">
						<button
							type="button"
							onClick={() => onStepClick(n)}
							aria-current={isActive ? "step" : undefined}
							aria-label={stepLabel(n, label)}
							aria-describedby={
								blocked?.step === n ? blocked.messageId : undefined
							}
							data-testid={`wizard-stepper-${n}`}
							className="group flex w-full flex-col gap-1.5 rounded-md px-0.5 pb-1 text-left"
						>
							<span
								aria-hidden="true"
								className={`h-1 w-full rounded-full transition-colors ${
									hasError
										? "bg-red-400"
										: isActive || isDone
											? "bg-brand-600"
											: "bg-gray-200 group-hover:bg-brand-200"
								}`}
							/>

							<span
								className={`truncate text-xs font-semibold ${
									isActive
										? "text-brand-700"
										: isDone
											? "text-gray-700"
											: "text-gray-500 group-hover:text-gray-600"
								}`}
							>
								{label}
							</span>
						</button>
					</li>
				);
			})}
		</ol>
	);
}

export function FloatingField({
	id,
	label,
	registration,
	required = false,
	error,
	maxLength,
	multiline = false,
	rows = 5,
	showCount = false,
	displayValue,
	wrapperClassName,
	inputMode,
	pattern,
}: {
	id: string;
	label: string;
	registration: UseFormRegisterReturn;
	required?: boolean;
	error?: string;
	maxLength?: number;
	multiline?: boolean;
	rows?: number;
	showCount?: boolean;

	displayValue?: string;
	wrapperClassName?: string;
	inputMode?: "numeric";
	pattern?: string;
}) {
	const fieldClass = `peer w-full rounded-xl border bg-white px-4 pb-2 pt-5 text-sm text-gray-900 shadow-sm transition ${fieldBorderClass(Boolean(error))}`;
	const labelClass = `pointer-events-none absolute left-4 top-1.5 text-xs font-medium transition-all peer-placeholder-shown:top-3.5 peer-placeholder-shown:text-sm peer-placeholder-shown:font-normal peer-placeholder-shown:text-gray-600 peer-focus:top-1.5 peer-focus:text-xs peer-focus:font-medium ${
		error
			? "text-red-600 peer-focus:text-red-600"
			: "text-gray-600 peer-focus:text-brand-700"
	}`;
	const errorId = error ? `${id}-error` : undefined;

	return (
		<div className={wrapperClassName}>
			<div className="relative">
				{multiline ? (
					<textarea
						id={id}
						rows={rows}
						maxLength={maxLength}
						placeholder=" "
						aria-invalid={error ? true : undefined}
						aria-describedby={errorId}
						aria-required={required || undefined}
						className={fieldClass}
						{...registration}
					/>
				) : (
					<input
						id={id}
						type="text"
						maxLength={maxLength}
						placeholder=" "
						inputMode={inputMode}
						pattern={pattern}
						aria-invalid={error ? true : undefined}
						aria-describedby={errorId}
						aria-required={required || undefined}
						className={fieldClass}
						{...registration}
					/>
				)}
				<label htmlFor={id} className={labelClass}>
					{label}
					{required && <RequiredMark />}
				</label>
			</div>
			{error ? (
				<p id={errorId} className="mt-1 text-xs text-red-600" role="alert">
					{error}
				</p>
			) : (
				showCount &&
				maxLength !== undefined && (
					<CharCount current={displayValue?.length ?? 0} max={maxLength} />
				)
			)}
		</div>
	);
}

interface ChoiceOption<T extends string> {
	value: T;
	label: string;
	id?: string;
}

/**
 * One question answered by picking one of a few options, drawn as a row of
 * compact segments rather than a grid of large cards: the wizard asks four
 * of these, and at card size they were most of what made it feel crowded
 * (#2387). A real fieldset with a legend, so each radio is announced with
 * its question. Driven either by a react-hook-form registration (string
 * fields) or by `value`/`onChange` (the remote flag, which is a boolean).
 */
export function ChoiceGroup<T extends string>({
	legend,
	name,
	options,
	value,
	onChange,
	registration,
	children,
}: {
	legend: string;
	name: string;
	options: readonly ChoiceOption<T>[];
	value: T;
	onChange?: (value: T) => void;
	registration?: UseFormRegisterReturn;
	children?: ReactNode;
}) {
	return (
		<fieldset>
			<legend className="mb-2 text-sm font-semibold text-gray-800">
				{legend}
			</legend>
			<div
				className={`grid gap-2 ${options.length > 2 ? "grid-cols-2 sm:grid-cols-4" : "grid-cols-2"}`}
			>
				{options.map((option) => (
					<label
						key={option.value}
						className={`flex min-h-11 cursor-pointer items-center justify-center rounded-xl border px-3 py-2 text-center text-sm font-medium transition ${
							value === option.value
								? "border-brand-600 bg-brand-50 text-brand-800"
								: "border-gray-500 bg-white text-gray-700 hover:bg-gray-50"
						}`}
					>
						<input
							type="radio"
							id={option.id}
							name={name}
							value={option.value}
							className="sr-only"
							{...(registration ?? {
								checked: value === option.value,
								onChange: () => onChange?.(option.value),
							})}
						/>
						{option.label}
					</label>
				))}
			</div>
			{children}
		</fieldset>
	);
}
