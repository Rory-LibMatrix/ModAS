namespace MxApiExtensions.Services;

/// <summary>
///    Configuration for ModAS.
/// </summary>
public class ModASConfiguration {
    public ModASConfiguration(IConfiguration configuration) {
        configuration.GetRequiredSection("ModAS").Bind(this);
    }

    public string ServerName { get; set; }
    public string HomeserverUrl { get; set; }

    public Dictionary<string, List<string>> Roles { get; set; }
}