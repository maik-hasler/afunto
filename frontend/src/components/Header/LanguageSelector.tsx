import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "react-oidc-context";
import { useApiClient } from "../../hooks/useApiClient";
import { useDismissableOverlay } from "../../hooks/useDismissableOverlay";
import { dispatchToast } from "../../lib/toastBus";
import { ChevronDownIcon } from "../icons";

const LANGUAGES = [
	{ code: "en", short: "EN" },
	{ code: "de", short: "DE" },
] as const;

type LangCode = (typeof LANGUAGES)[number]["code"];

export default function LanguageSelector({
	transparent = false,
}: {
	transparent?: boolean;
}) {
	const { i18n, t } = useTranslation();
	const auth = useAuth();
	const api = useApiClient();
	const [open, setOpen] = useState(false);
	const ref = useDismissableOverlay<HTMLDivElement>(open, () => setOpen(false));

	const currentCode: LangCode = LANGUAGES.some((l) => l.code === i18n.language)
		? (i18n.language as LangCode)
		: "en";
	const current = LANGUAGES.find((l) => l.code === currentCode) ?? LANGUAGES[0];

	// The interface language and the account's email language are two separate
	// settings, and switching one used to change nothing about the other with
	// no word to the user - so an English interface kept sending German email
	// (#2328). Say so at the moment the two diverge; the profile is where it
	// gets changed.
	async function warnIfEmailLanguageDiffers(uiLanguage: LangCode) {
		if (!auth.isAuthenticated) return;
		try {
			const profile = await api.getUserProfile({});
			const emailLanguage = profile.preferredLanguage === "en" ? "en" : "de";
			if (emailLanguage === uiLanguage) return;
			dispatchToast(
				"info",
				i18n.t("language.emailLanguageDiffers", {
					language: i18n.t(
						emailLanguage === "en"
							? "language.contentEn"
							: "language.contentDe",
					),
				}),
			);
		} catch {
			// A failed lookup is not worth turning a language switch into an
			// error - the profile hint still states the divergence.
		}
	}

	async function selectLanguage(code: LangCode) {
		setOpen(false);
		if (code === currentCode) return;
		await i18n.changeLanguage(code);
		localStorage.setItem("afunto:language-explicit", "true");
		await warnIfEmailLanguageDiffers(code);
	}

	return (
		<div className="relative" ref={ref}>
			<button
				type="button"
				onClick={() => setOpen((o) => !o)}
				aria-expanded={open}

				aria-label={t("language.switchLanguageCurrent", {
					code: current.short,
					language: t(`language.${currentCode}`),
				})}
				data-testid="language-selector-trigger"
				className={`flex h-11 items-center gap-1.5 rounded-xl border px-3 text-sm transition-colors ${transparent ? "border-white/50 text-white hover:bg-white/10" : "border-gray-500 text-gray-700 hover:bg-gray-50"}`}
			>
				<span className="font-semibold tracking-wide">{current.short}</span>
				<ChevronDownIcon
					open={open}
					className={`h-3.5 w-3.5 ${transparent ? "text-white/70" : "text-gray-400"}`}
				/>
			</button>

			{open && (
				<ul
					aria-label={t("language.switchLanguage")}
					data-testid="language-selector-menu"
					className={`absolute top-full right-0 z-50 mt-1 w-36 rounded-lg border py-1 shadow-modal ${transparent ? "border-white/20 bg-brand-800" : "border-gray-200 bg-white"}`}
				>
					{LANGUAGES.map((lang) => (
						<li key={lang.code}>
							<button
								type="button"
								aria-current={lang.code === currentCode ? "true" : undefined}
								onClick={() => void selectLanguage(lang.code)}
								className={`flex min-h-11 w-full items-center gap-2.5 px-3 py-2 text-sm transition-colors ${
									transparent
										? lang.code === currentCode
											? "bg-white/15 font-medium text-white"
											: "text-white/80 hover:bg-white/10 hover:text-white"
										: lang.code === currentCode
											? "bg-brand-50 font-medium text-brand-700"
											: "text-gray-700 hover:bg-gray-50"
								}`}
							>
								<span
									aria-hidden="true"
									className={`rounded border px-1 py-0.5 text-xs leading-none font-bold tracking-wide ${
										transparent
											? lang.code === currentCode
												? "border-white/50 text-white"
												: "border-white/30 text-white/80"
											: lang.code === currentCode
												? "border-brand-300 text-brand-700"
												: "border-gray-300 text-gray-600"
									}`}
								>
									{lang.short}
								</span>
								<span>{t(`language.${lang.code}`)}</span>
								{lang.code === currentCode && (
									<span
										className={`ml-auto h-1.5 w-1.5 rounded-full ${transparent ? "bg-white/60" : "bg-brand-500"}`}
									/>
								)}
							</button>
						</li>
					))}
				</ul>
			)}
		</div>
	);
}
