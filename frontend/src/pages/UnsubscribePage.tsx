import { useTranslation } from "react-i18next";
import { useNoIndex } from "../hooks/useNoIndex";
import { usePageTitle } from "../hooks/usePageTitle";
import Button from "../components/Button";
import { BellIcon } from "../components/icons";
import { statusTitleClass } from "../lib/headingClasses";
import { cardClass } from "../lib/surfaceClasses";

export default function UnsubscribePage() {
	const { t } = useTranslation();
	usePageTitle(t("unsubscribe.title"));
	useNoIndex();

	return (
		<div className="mx-auto max-w-md py-10 sm:py-16">
			<div className={`${cardClass} text-center sm:p-8`}>
				<div
					aria-hidden="true"
					className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-brand-100 text-brand-700"
				>
					<BellIcon className="h-7 w-7" />
				</div>
				<h1 className={`mt-6 text-gray-900 ${statusTitleClass}`}>
					{t("unsubscribe.title")}
				</h1>
				<p className="mt-4 leading-relaxed text-gray-700">
					{t("unsubscribe.description")}
				</p>
				<Button to="/" size="lg" className="mt-8">
					{t("notFound.backHome")}
				</Button>
			</div>
		</div>
	);
}
