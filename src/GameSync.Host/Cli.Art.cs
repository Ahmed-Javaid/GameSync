namespace GameSync.Host;

public static partial class Cli
{
    /// <summary>
    /// ART-01, ART-07: each game's cover, hero and logo: from the Steam client's own cache on this PC first, and from
    /// Steam's store only for new games and art the Steam client doesn't have. <c>--refresh</c> asks the store about every game.
    /// </summary>
    private static async Task<int> ArtAsync(string dataDir, List<string> rest)
    {
        var askAll = rest.Remove("--refresh");
        var (games, refresh) = await LauncherData.FetchArtAsync(dataDir, CancellationToken.None, askAll);
        if (games == 0)
        {
            Console.WriteLine("No game here has a Steam app ID, so every tile shows its title cover.");
            return 0;
        }

        Console.WriteLine($"{games} games have a Steam app ID.");
        Console.WriteLine($"  {refresh.Copied} images taken from Steam's own cache on this PC, with no download.");
        Console.WriteLine(refresh.Asked == 0
            ? "  Steam's store wasn't asked: nothing new."
            : $"  Asked Steam's store about {refresh.Asked} games, by app ID only; {refresh.Downloaded} images downloaded.");
        return 0;
    }
}
