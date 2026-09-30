namespace Infrastructure.Maps;

internal sealed class MapTileOptions
{
	public string BaseUrl { get; set; } = "https://tile.openstreetmap.org";

	public string UserAgent { get; set; } = "Afunto/1.0 (https://github.com/maik-hasler/afunto)";

	public int TimeoutSeconds { get; set; } = 5;

	public int MaxZoom { get; set; } = 19;

	public int CacheDurationMinutes { get; set; } = 1440;
}
