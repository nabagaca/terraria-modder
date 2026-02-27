namespace TerrariaModManager.Services;

public class NxmLink
{
    public string GameDomain { get; set; } = "";
    public int ModId { get; set; }
    public int FileId { get; set; }
    public string? Key { get; set; }
    public long? Expires { get; set; }
}

public class NxmLinkHandler
{
    public NxmLink? Parse(string uri)
    {
        // nxm://terraria/mods/135/files/12345?key=abc&expires=123456&user_id=789
        if (string.IsNullOrWhiteSpace(uri)) return null;

        try
        {
            if (!uri.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                return null;

            // Uri class doesn't handle nxm:// well, parse manually
            var withoutScheme = uri["nxm://".Length..];
            var queryIdx = withoutScheme.IndexOf('?');

            string path;
            string query = "";

            if (queryIdx >= 0)
            {
                path = withoutScheme[..queryIdx];
                query = withoutScheme[(queryIdx + 1)..];
            }
            else
            {
                path = withoutScheme;
            }

            // Parse path: terraria/mods/135/files/12345
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 5) return null;
            if (segments[1] != "mods" || segments[3] != "files") return null;

            if (!int.TryParse(segments[2], out var modId)) return null;
            if (!int.TryParse(segments[4], out var fileId)) return null;

            var link = new NxmLink
            {
                GameDomain = segments[0],
                ModId = modId,
                FileId = fileId
            };

            // Parse query params
            if (!string.IsNullOrEmpty(query))
            {
                foreach (var param in query.Split('&'))
                {
                    var kv = param.Split('=', 2);
                    if (kv.Length != 2) continue;

                    switch (kv[0])
                    {
                        case "key":
                            link.Key = Uri.UnescapeDataString(kv[1]);
                            break;
                        case "expires":
                            if (long.TryParse(kv[1], out var exp))
                                link.Expires = exp;
                            break;
                    }
                }
            }

            return link;
        }
        catch
        {
            return null;
        }
    }

    public bool IsTerrariaLink(NxmLink link) =>
        string.Equals(link.GameDomain, "terraria", StringComparison.OrdinalIgnoreCase);
}
