import re

with open('src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs', 'r') as f:
    content = f.read()

# 1. Add using
if 'using System.Collections.Generic;' not in content:
    content = 'using System.Collections.Generic;\n' + content
if 'using System.Collections.ObjectModel;' not in content:
    content = 'using System.Collections.ObjectModel;\n' + content

# 2. Constructor
content = content.replace('IOptions<AzureDevOpsOptions> devOpsOptions,', 'AzureDevOps.Core.Interfaces.ILogRepository logRepository,\n        IOptions<AzureDevOpsOptions> devOpsOptions,')
content = content.replace('_catalogRepository = catalogRepository;', '_catalogRepository = catalogRepository;\n        _logRepository = logRepository;')
content = content.replace('private readonly ICatalogRepository _catalogRepository;', 'private readonly ICatalogRepository _catalogRepository;\n    private readonly AzureDevOps.Core.Interfaces.ILogRepository _logRepository;')

# 3. Auth properties
auth_props = """
    private bool _useDefaultCredentials = true;
    public bool UseDefaultCredentials { get => _useDefaultCredentials; set { _useDefaultCredentials = value; OnPropertyChanged(); } }
    private string _authDomain = string.Empty;
    public string AuthDomain { get => _authDomain; set { _authDomain = value; OnPropertyChanged(); } }
    private string _authUsername = string.Empty;
    public string AuthUsername { get => _authUsername; set { _authUsername = value; OnPropertyChanged(); } }
    private string _authPassword = string.Empty;
    public string AuthPassword { get => _authPassword; set { _authPassword = value; OnPropertyChanged(); } }
"""
content = content.replace('private string _token = string.Empty;', 'private string _token = string.Empty;' + auth_props)

# 4. Log properties
log_props = """
    public ObservableCollection<AzureDevOps.Core.Interfaces.SystemLogEntity> CurrentLogs { get; } = new();
    private List<DateTime> _availableLogDates = new();
    private int _currentLogDateIndex = 0;
    
    private string _currentLogDateLabel = "Sin Logs";
    public string CurrentLogDateLabel
    {
        get => _currentLogDateLabel;
        set { _currentLogDateLabel = value; OnPropertyChanged(); }
    }
"""
content = content.replace('public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];', 'public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];' + log_props)

# 5. Save config
save_repl = """            var config = new AzureDevOpsOptions
            {
                BaseUrl = this.BaseUrl,
                Collection = this.Collection,
                ApiVersion = this.ApiVersion,
                Auth = new AzureDevOpsAuthOptions
                {
                    UseDefaultCredentials = this.UseDefaultCredentials,
                    Domain = string.IsNullOrWhiteSpace(this.AuthDomain) ? null : this.AuthDomain,
                    Username = string.IsNullOrWhiteSpace(this.AuthUsername) ? null : this.AuthUsername,
                    Password = string.IsNullOrWhiteSpace(this.AuthPassword) ? null : this.AuthPassword
                }
            };
            var configJson = System.Text.Json.JsonSerializer.Serialize(config);
            await Task.Run(() => _configurationRepository.SetConfigurationAsync("SystemConfig", configJson, ct), ct);"""
content = re.sub(r'var configJson = System.Text.Json.JsonSerializer.Serialize\(new.*?await _configurationRepository.SetConfigurationAsync\("SystemConfig", configJson, ct\);', save_repl, content, flags=re.DOTALL)

# 6. Test connection
test_repl = """            var handler = new System.Net.Http.HttpClientHandler();
            if (UseDefaultCredentials)
            {
                handler.UseDefaultCredentials = true;
            }
            else
            {
                handler.Credentials = new System.Net.NetworkCredential(AuthUsername, AuthPassword, AuthDomain);
            }
            using var httpClient = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };"""
content = content.replace('using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };', test_repl)

# 7. Log methods - add at the END of the class before the last brace
log_methods = """
    public async Task LoadAvailableLogDatesAsync(System.Threading.CancellationToken ct = default)
    {
        _availableLogDates = await Task.Run(() => _logRepository.GetAvailableDatesAsync(ct));
        if (_availableLogDates.Count > 0)
        {
            _currentLogDateIndex = 0;
            await LoadLogsForCurrentDateAsync();
        }
    }

    public async Task NextLogDateAsync()
    {
        if (_currentLogDateIndex > 0)
        {
            _currentLogDateIndex--;
            await LoadLogsForCurrentDateAsync();
        }
    }

    public async Task PrevLogDateAsync()
    {
        if (_currentLogDateIndex < _availableLogDates.Count - 1)
        {
            _currentLogDateIndex++;
            await LoadLogsForCurrentDateAsync();
        }
    }

    private async Task LoadLogsForCurrentDateAsync()
    {
        if (_availableLogDates.Count == 0) return;
        var date = _availableLogDates[_currentLogDateIndex];
        CurrentLogDateLabel = date.ToString("yyyy-MM-dd");
        var logs = await Task.Run(() => _logRepository.GetLogsByDateAsync(date));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            CurrentLogs.Clear();
            foreach (var log in logs) CurrentLogs.Add(log);
        });
    }
"""
# Replace the LAST brace with log_methods + '\n}'
# We can do this by finding the last instance of '}'
last_brace_index = content.rfind('}')
if last_brace_index != -1:
    content = content[:last_brace_index] + log_methods + content[last_brace_index:]

with open('src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs', 'w') as f:
    f.write(content)

