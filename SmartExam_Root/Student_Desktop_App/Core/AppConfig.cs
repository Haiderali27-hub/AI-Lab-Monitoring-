using System.Text.Json;
using System.IO;

namespace Student_Desktop_App.Core;

public class AppConfig
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7068";
    public int HeartbeatSeconds { get; set; } = 10;
}

public static class AppConfigProvider
{
    private static readonly Lazy<AppConfig> LazyConfig = new(LoadConfig);

    public static AppConfig Current => LazyConfig.Value;

    private static AppConfig LoadConfig()
    {
        var config = new AppConfig();

        var envBaseUrl = Environment.GetEnvironmentVariable("SMARTEXAM_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(envBaseUrl))
        {
            config.ApiBaseUrl = envBaseUrl;
        }

        var envHeartbeat = Environment.GetEnvironmentVariable("SMARTEXAM_HEARTBEAT_SECONDS");
        if (int.TryParse(envHeartbeat, out var heartbeatSeconds) && heartbeatSeconds > 1)
        {
            config.HeartbeatSeconds = heartbeatSeconds;
        }

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.desktop.json");
        if (!File.Exists(configPath))
        {
            return config;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var fileConfig = JsonSerializer.Deserialize<AppConfig>(json);
            if (fileConfig is not null)
            {
                config.ApiBaseUrl = string.IsNullOrWhiteSpace(fileConfig.ApiBaseUrl)
                    ? config.ApiBaseUrl
                    : fileConfig.ApiBaseUrl;

                if (fileConfig.HeartbeatSeconds > 1)
                {
                    config.HeartbeatSeconds = fileConfig.HeartbeatSeconds;
                }
            }
        }
        catch
        {
            // Ignore malformed local config and continue with safe defaults.
        }

        return config;
    }
}