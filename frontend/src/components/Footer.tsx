import { Link, useLocation } from "react-router";
import { Trans, useTranslation } from "react-i18next";
import Button from "./Button";
import { WAVE_PATH } from "../lib/wavePath";
import { runtimeConfig } from "../lib/runtimeConfig";

export default function Footer({
	compact = false,
	headingLevel = 2,
}: {
	compact?: boolean;

	headingLevel?: 2 | 3;
}) {
	const { t } = useTranslation();
	const location = useLocation();
	const currentYear = new Date().getFullYear();
	const Heading = headingLevel === 3 ? "h3" : "h2";

	const showCta = location.pathname !== "/opportunities";

	if (compact) {
		return (
			<footer className="border-t border-gray-200 bg-white py-4 text-center text-xs text-gray-500">
				<Link to="/imprint" className="inline-block py-1 hover:text-gray-600">
					{t("footer.imprint")}
				</Link>
				<span className="mx-2">&middot;</span>
				<Link
					to="/terms-of-use"
					className="inline-block py-1 hover:text-gray-600"
				>
					{t("footer.terms")}
				</Link>
				<span className="mx-2">&middot;</span>
				<Link
					to="/privacy-policy"
					className="inline-block py-1 hover:text-gray-600"
				>
					{t("footer.privacy")}
				</Link>
				<span className="mx-2">&middot;</span>
				<Link to="/contact" className="inline-block py-1 hover:text-gray-600">
					{t("footer.contact")}
				</Link>
				<span className="mx-2">&middot;</span>
				<Link to="/help" className="inline-block py-1 hover:text-gray-600">
					{t("footer.help")}
				</Link>
				<span className="mx-2">&middot;</span>
				<a
					href="https://github.com/maik-hasler/afunto/blob/main/LICENSE"
					target="_blank"
					rel="noopener noreferrer"
					className="inline-block py-1 hover:text-gray-600"
				>
					{t("footer.license")}
				</a>
				<span className="mx-2">&middot;</span>
				<span className="inline-block py-1">{`v${runtimeConfig.appVersion}`}</span>
			</footer>
		);
	}

	return (
		<footer>
			<svg
				aria-hidden="true"
				viewBox="0 0 1440 60"
				preserveAspectRatio="none"
				className="block h-8 w-full text-brand-50 sm:h-12"
			>
				<path d={WAVE_PATH} fill="currentColor" />
			</svg>
			<div className="bg-brand-50">
				<div className="mx-auto max-w-page px-4 pt-6 pb-12 sm:px-6 lg:px-8">
					<div
						className={
							showCta ? "grid grid-cols-1 gap-8 lg:grid-cols-3" : undefined
						}
					>
						{showCta && (
							<div className="relative isolate overflow-hidden rounded-card bg-brand-100 p-8 shadow-resting sm:p-10 lg:col-span-1">
								<div
									aria-hidden="true"
									className="pointer-events-none absolute -top-10 -right-14 h-40 w-40 rounded-full bg-white/30"
								/>
								<div
									aria-hidden="true"
									className="pointer-events-none absolute -bottom-16 -left-10 h-32 w-32 rounded-full bg-brand-600/20"
								/>
								<div className="relative">
									<Heading className="font-display text-2xl font-bold text-brand-900 sm:text-3xl">
										{t("footer.ctaTitle")}
									</Heading>
									<p className="mt-4 text-base leading-relaxed text-brand-800">
										{t("brand.description")}
									</p>
									<Button
										href="/opportunities"
										variant="primary"
										size="lg"
										className="mt-8 shadow-md"
									>
										{t("footer.ctaButton")}
									</Button>
								</div>
							</div>
						)}

						{/* Centred in the row rather than top-aligned: the CTA card is
						352px tall against ~120px of links, and hanging the lists off the
						top of that left the bottom two thirds of the footer looking like
						a column that failed to load (#2329 F13). */}
						<div
							className={showCta ? "lg:col-span-2 lg:self-center" : undefined}
						>
							<div className="grid grid-cols-1 gap-8 sm:grid-cols-3">
								<div>
									<Heading className="mb-4 text-xs font-semibold tracking-wider text-gray-900 uppercase">
										{t("footer.platform")}
									</Heading>
									<ul className="space-y-2 text-sm">
										<li>
											<Link
												to="/opportunities"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.findOpportunities")}
											</Link>
										</li>
										<li>
											<Link
												to="/organizations"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.browseOrganizations")}
											</Link>
										</li>
									</ul>
								</div>

								<div>
									<Heading className="mb-4 text-xs font-semibold tracking-wider text-gray-900 uppercase">
										{t("footer.support")}
									</Heading>
									<ul className="space-y-2 text-sm">
										<li>
											<Link
												to="/help"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.help")}
											</Link>
										</li>
										<li>
											<Link
												to="/contact"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.contact")}
											</Link>
										</li>
									</ul>
								</div>

								<div>
									<Heading className="mb-4 text-xs font-semibold tracking-wider text-gray-900 uppercase">
										{t("footer.legal")}
									</Heading>
									<ul className="space-y-2 text-sm">
										<li>
											<Link
												to="/imprint"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.imprint")}
											</Link>
										</li>
										<li>
											<Link
												to="/terms-of-use"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.terms")}
											</Link>
										</li>
										<li>
											<Link
												to="/privacy-policy"
												className="inline-block py-0.5 text-gray-600 transition-colors hover:text-brand-700"
											>
												{t("footer.privacy")}
											</Link>
										</li>
									</ul>
								</div>
							</div>
						</div>
					</div>

					{/* Full width, below the grid. Pinned to the bottom of the
					stretched link column it opened a ~100px hole at 1024, where the
					352px CTA card sets the row height and the tallest link column is
					~120px (#2329 F13). */}
					<div className="mt-10 flex flex-col gap-3 border-t border-brand-100 pt-6 text-xs text-gray-500 sm:flex-row sm:items-center sm:justify-between">
						<div className="flex items-center gap-3">
							<a
								href="https://github.com/maik-hasler/afunto"
								target="_blank"
								rel="noopener noreferrer"
								aria-label="GitHub"
								className="-m-1.5 inline-flex p-1.5 text-gray-600 transition-colors hover:text-brand-700"
							>
								<svg
									className="h-5 w-5"
									fill="currentColor"
									viewBox="0 0 24 24"
									aria-hidden="true"
								>
									<path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z" />
								</svg>
							</a>
							<span>{`v${runtimeConfig.appVersion}`}</span>
						</div>
						<p>
							<Trans
								i18nKey="footer.copyright"
								values={{ year: currentYear }}
								components={{
									licenseLink: (
										// eslint-disable-next-line jsx-a11y/anchor-has-content
										<a
											href="https://github.com/maik-hasler/afunto/blob/main/LICENSE"
											target="_blank"
											rel="noopener noreferrer"
											className="inline-block py-1 underline hover:text-brand-700"
										/>
									),
								}}
							/>
						</p>
					</div>
				</div>
			</div>
		</footer>
	);
}
