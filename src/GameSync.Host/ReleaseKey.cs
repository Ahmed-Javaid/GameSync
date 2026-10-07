namespace GameSync.Host;

/// <summary>
/// R19: the public half of GameSync's release key, which every update's signature must check out with. Made by
/// <c>tools/GameSync.Release keygen</c> on 7 Oct 2026, which writes this file; its private half stays with the owner,
/// out of the repository.
/// </summary>
public static class ReleaseKey
{
    public const string Public = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEB8wlRYqwe7XZYBUSCKT9zOP/MDKEDauipht+XgQbUW1ywQqUwlXOZv4e+0ECr5XEY/y1G+T7m08mk3W5ZlQnjQ==";
}
