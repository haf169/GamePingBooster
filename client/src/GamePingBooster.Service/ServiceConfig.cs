using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamePingBooster.Service;

/// <summary>
/// Service configuration, read from %ProgramData%\GamePingBooster\config.json.
/// The installer writes this file; the UI never edits it directly and sends commands over the
/// named pipe instead.
/// </summary>
public sealed class ServiceConfig
{
    /// <summary>URL to fetch the profile (game IP ranges plus relay list). Empty = local file only.</summary>
    [JsonPropertyName("profileUrl")] public string? ProfileUrl { get; set; }

    /// <summary>Path to the local profile file, used offline or when the fetch fails.</summary>
    [JsonPropertyName("profilePath")] public string ProfilePath { get; set; } = "profiles/pubg-vn.json";

    /// <summary>Pre-shared key; default fallback key provided so client is always ready.</summary>
    [JsonPropertyName("psk")] public string Psk { get; set; } = "gpb-custom-psk";

    /// <summary>Licence server URL. Always empty in this custom standalone version.</summary>
    [JsonPropertyName("licenceUrl")] public string? LicenceUrl { get; set; } = "";

    /// <summary>Default relay id; empty means take the first relay in the profile.</summary>
    [JsonPropertyName("defaultRelayId")] public string? DefaultRelayId { get; set; }

    /// <summary>
    /// Self-hosted relays, set from the settings screen. Each is "host:port".
    /// </summary>
    [JsonPropertyName("relayEndpoints")] public List<string> RelayEndpoints { get; set; } = [];

    /// <summary>
    /// Always true in standalone version so connect is never blocked by key absence.
    /// </summary>
    [JsonIgnore]
    public bool HasKey => true;

    /// <summary>
    /// The game relays are measured for at connect when no game is open and none has been seen
    /// yet. Only a fallback: which game is accelerated is decided by which process is running.
    /// </summary>
    [JsonPropertyName("defaultGameId")] public string DefaultGameId { get; set; } = "pubg";

    /// <summary>
    /// The last game the service saw running, written when it changes.
    ///
    /// Relays are measured once, at connect, against one game's region - usually before any game is
    /// open. Remembering the last one means somebody who plays Counter-Strike 2 gets relays chosen
    /// for Counter-Strike 2 from their second session on, without choosing anything.
    /// </summary>
    [JsonPropertyName("lastGameId")] public string? LastGameId { get; set; }

    /// <summary>Virtual adapter name as shown in Network Connections.</summary>
    [JsonPropertyName("adapterName")] public string AdapterName { get; set; } = "Game Ping Booster";

    /// <summary>
    /// true = install routes immediately on connect without waiting for the game. Debug only -
    /// leaving routes in place permanently would drag unrelated traffic through the relay.
    /// </summary>
    [JsonPropertyName("routeWithoutGame")] public bool RouteWithoutGame { get; set; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GamePingBooster");

    private static string FilePath => Path.Combine(DefaultDirectory, "config.json");

    /// <summary>
    /// Writes the configuration back, atomically.
    ///
    /// Via a temporary file and a replace, because the alternative is a half-written config.json
    /// if the machine loses power mid-save - and a service that cannot parse its own
    /// configuration does not start, which turns a settings change into a dead installation.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(DefaultDirectory);
        var json = JsonSerializer.Serialize(this, ServiceConfigJsonContext.Default.ServiceConfig);

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static ServiceConfig Load()
    {
        var path = Path.Combine(DefaultDirectory, "config.json");
        if (!File.Exists(path))
        {
            // During development we run straight out of the build directory.
            path = Path.Combine(AppContext.BaseDirectory, "config.json");
        }
        if (!File.Exists(path))
        {
            // Not an error any more. A freshly installed machine has no configuration, and the
            // service has to come up anyway so the UI can connect and offer the settings screen.
            // Throwing here meant the service died on first run and the user saw nothing at all.
            return new ServiceConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, ServiceConfigJsonContext.Default.ServiceConfig)
               ?? throw new InvalidOperationException($"config.json at {path} is not valid.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceConfig))]
public partial class ServiceConfigJsonContext : JsonSerializerContext;
