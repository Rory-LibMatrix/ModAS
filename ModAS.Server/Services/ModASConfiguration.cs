namespace MxApiExtensions.Services;


public class ModASConfiguration {
    public ModASConfiguration(IConfiguration configuration) {
        configuration.GetRequiredSection("ModAS").Bind(this);
    }
    public string ServerName { get; set; }
    public string HomeserverUrl { get; set; }
}