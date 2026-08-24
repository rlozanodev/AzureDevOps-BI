import re

with open('src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs', 'r') as f:
    content = f.read()

load_method = """    public async Task LoadConfigurationAsync()
    {
        try
        {
            var configJson = await Task.Run(() => _configurationRepository.GetConfigurationAsync("SystemConfig"));
            if (!string.IsNullOrWhiteSpace(configJson))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<AzureDevOps.Core.Configuration.AzureDevOpsOptions>(configJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                {
                    if (!string.IsNullOrEmpty(config.BaseUrl)) BaseUrl = config.BaseUrl;
                    if (!string.IsNullOrEmpty(config.Collection)) Collection = config.Collection;
                    if (!string.IsNullOrEmpty(config.ApiVersion)) ApiVersion = config.ApiVersion;
                    if (config.Auth != null)
                    {
                        UseDefaultCredentials = config.Auth.UseDefaultCredentials;
                        AuthDomain = config.Auth.Domain ?? "";
                        AuthUsername = config.Auth.Username ?? "";
                        AuthPassword = config.Auth.Password ?? "";
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from DB on startup.");
        }
    }

    public async Task SaveConfigurationAsync"""

content = content.replace("    public async Task SaveConfigurationAsync", load_method)

with open('src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs', 'w') as f:
    f.write(content)

