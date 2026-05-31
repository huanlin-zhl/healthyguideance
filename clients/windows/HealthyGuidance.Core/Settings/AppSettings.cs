namespace HealthyGuidance.Core.Settings;

public class AppSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(DeploymentName)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
