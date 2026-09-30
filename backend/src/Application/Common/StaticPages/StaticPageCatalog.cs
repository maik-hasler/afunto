namespace Application.Common.StaticPages;

/// <summary>
/// The SPA's data-less routes, in the order a crawler should meet them.
/// <para>
/// These pages exist only in the frontend bundle, so nothing on the backend used
/// to know about them: the sitemap listed database-backed entity URLs and no
/// static page at all - not even the site root - and every static route shared
/// index.html's single hardcoded set of Open Graph tags, which name the homepage
/// as their canonical URL (afunto#2331). One catalog feeds both, so a new
/// static route cannot be added to one and forgotten in the other.
/// </para>
/// <para>
/// Copy is German because that is the language the SPA shell and the per-entity
/// crawler HTML (see <see cref="Meta.MetaHtmlBuilder"/>) already declare.
/// Deliberately excluded: /callback and /unsubscribe(d), which only make sense
/// as the end of a flow the visitor is already in, and every authenticated route.
/// </para>
/// </summary>
public static class StaticPageCatalog
{
	public static IReadOnlyList<StaticPage> All { get; } =
	[
		new(
			"home",
			"/",
			"Afunto - Spontan Freiwilligenarbeit leisten. Finde deinen Einsatz.",
			"Afunto verbindet engagierte Freiwillige mit regionalen Hilfsangeboten. "
			+ "Finde lokale Einsätze, hilf spontan und mach einen Unterschied in deiner Gemeinde."),
		new(
			"opportunities",
			"/opportunities",
			"Einsätze finden - Afunto",
			"Finde einen Einsatz in deiner Nähe und pack mit an. "
			+ "Die meisten dauern nur wenige Stunden."),
		new(
			"organizations",
			"/organizations",
			"Organisationen - Afunto",
			"Finde Organisationen auf Afunto, die du schon kennst."),
		new(
			"help",
			"/help",
			"Hilfe - Afunto",
			"Antworten auf häufige Fragen für Freiwillige und Organisationen auf Afunto."),
		new(
			"contact",
			"/contact",
			"Kontakt - Afunto",
			"Melde ein Problem oder finde die richtige Stelle für deine Frage."),
		new(
			"imprint",
			"/imprint",
			"Impressum - Afunto",
			"Anbieterkennzeichnung und Kontaktdaten des Betreibers von Afunto."),
		new(
			"privacy-policy",
			"/privacy-policy",
			"Datenschutzerklärung - Afunto",
			"Wie Afunto personenbezogene Daten erhebt, verarbeitet und schützt, "
			+ "und welche Rechte du dabei hast."),
		new(
			"terms-of-use",
			"/terms-of-use",
			"Nutzungsbedingungen - Afunto",
			"Die Bedingungen für die Nutzung von Afunto."),
	];

	public static StaticPage? Find(string slug) =>
		All.FirstOrDefault(page => string.Equals(page.Slug, slug, StringComparison.Ordinal));
}
