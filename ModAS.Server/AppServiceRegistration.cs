using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ModAS.Server;

public class AppServiceRegistration {
    private const string ValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const string ExtendedValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~+/";

    [JsonPropertyName("as_token")]
    public string AppServiceToken { get; set; } = RandomNumberGenerator.GetString(ExtendedValidChars, RandomNumberGenerator.GetInt32(512, 1024));

    [JsonPropertyName("hs_token")]
    public string HomeserverToken { get; set; } = RandomNumberGenerator.GetString(ExtendedValidChars, RandomNumberGenerator.GetInt32(512, 1024));

    [JsonPropertyName("id")]
    public string Id { get; set; } = "ModAS-" + RandomNumberGenerator.GetString(ValidChars, 5);

    [JsonPropertyName("namespaces")]
    public NamespacesObject Namespaces { get; set; } = new() {
        Users = [new() { Exclusive = false, Regex = "@.*" }],
        Aliases = [new() { Exclusive = false, Regex = "#.*" }],
        Rooms = [new() { Exclusive = false, Regex = "!.*" }]
    };

    [JsonPropertyName("protocols")]
    public Collection<string> Protocols { get; set; } = new(new[] {
        "ModAS"
    });

    [JsonPropertyName("rate_limited")]
    public bool RateLimited { get; set; } = false;

    [JsonPropertyName("sender_localpart")]
    public string SenderLocalpart { get; set; } = "ModAS";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "http://localhost:5071";

    public class NamespacesObject {
        [JsonPropertyName("users")]
        public List<NamespaceObject> Users { get; set; } = new();

        [JsonPropertyName("aliases")]
        public List<NamespaceObject> Aliases { get; set; } = new();

        [JsonPropertyName("rooms")]
        public List<NamespaceObject> Rooms { get; set; } = new();
    }

    public class NamespaceObject {
        [JsonPropertyName("exclusive")]
        public bool Exclusive { get; set; } = false;

        [JsonPropertyName("regex")]
        public string Regex { get; set; } = "*";
    }
}