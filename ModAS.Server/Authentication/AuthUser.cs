using LibMatrix.Homeservers;

namespace ModAS.Server.Authentication;

public class AuthUser {
    public required string AccessToken { get; set; }
    public required List<string> Roles { get; set; }
    public required AuthenticatedHomeserverGeneric Homeserver { get; set; }
}