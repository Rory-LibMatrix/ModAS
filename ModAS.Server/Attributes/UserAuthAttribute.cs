using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModAS.Server.Attributes;

public class UserAuthAttribute : Attribute {
    public AuthType AuthType { get; set; }
    public AuthRoles AnyRoles { get; set; }

    public string ToJson() => JsonSerializer.Serialize(new {
        AuthType,
        AnyRoles
    });
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthType {
    User,
    Server
}

[JsonConverter(typeof(JsonStringEnumConverter))]
[Flags]
public enum AuthRoles {
    Administrator = 1 << 0,
    Developer = 1 << 1,
}