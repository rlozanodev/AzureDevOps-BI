using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Entities;
using AzureDevOps.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOps.DesktopManager.ViewModels;

/// <summary>
/// ViewModel editable para una fila de proyecto en el DataGrid del catálogo.
/// </summary>
public class ProjectRowViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _accessStatus = string.Empty;

    public Guid ProjectId { get; init; }
    public string CollectionName { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;

    public string AccessStatus
    {
        get => _accessStatus;
        set { _accessStatus = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static ProjectRowViewModel FromEntity(CatalogProjectEntity e) => new()
    {
        ProjectId = e.ProjectId,
        CollectionName = e.CollectionName,
        ProjectName = e.ProjectName,
        AccessStatus = e.AccessStatus,
        IsEnabled = e.IsEnabled
    };
}

/// <summary>
/// ViewModel principal de la ventana. Gestiona el catálogo de proyectos,
/// la configuración de Azure DevOps y el estado de conexión a la DB.
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly AzureDevOps.Core.Interfaces.ILogRepository _logRepository;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AzureDevOpsOptions _devOpsOptions;

    private string _statusMessage = "Listo.";
    private bool _isBusy;
    private bool _isDbConnected;

    private string _baseUrl = string.Empty;
    private string _collection = string.Empty;
    private string _apiVersion = string.Empty;
    private string _token = string.Empty;
    private bool _useDefaultCredentials = true;
    public bool UseDefaultCredentials { get => _useDefaultCredentials; set { _useDefaultCredentials = value; OnPropertyChanged(); } }
    private string _authDomain = string.Empty;
    public string AuthDomain { get => _authDomain; set { _authDomain = value; OnPropertyChanged(); } }
    private string _authUsername = string.Empty;
    public string AuthUsername { get => _authUsername; set { _authUsername = value; OnPropertyChanged(); } }
    private string _authPassword = string.Empty;
    public string AuthPassword { get => _authPassword; set { _authPassword = value; OnPropertyChanged(); } }


    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];
    public ObservableCollection<AzureDevOps.Core.Interfaces.SystemLogEntity> CurrentLogs { get; } = new();
    private List<DateTime> _availableLogDates = new();
    private int _currentLogDateIndex = 0;
    
    private string _currentLogDateLabel = "Sin Logs";
    public string CurrentLogDateLabel
    {
        get => _currentLogDateLabel;
        set { _currentLogDateLabel = value; OnPropertyChanged(); }
    }


    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool IsDbConnected
    {
        get => _isDbConnected;
        private set { _isDbConnected = value; OnPropertyChanged(); }
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set { _baseUrl = value; OnPropertyChanged(); }
    }

    public string Collection
    {
        get => _collection;
        set { _collection = value; OnPropertyChanged(); }
    }

    public string ApiVersion
    {
        get => _apiVersion;
        set { _apiVersion = value; OnPropertyChanged(); }
    }

    public string Token
    {
        get => _token;
        set { _token = value; OnPropertyChanged(); }
    }

    public MainWindowViewModel(
        ICatalogRepository catalogRepository,
        IConfigurationRepository configurationRepository,
        AzureDevOps.Core.Interfaces.ILogRepository logRepository,
        IOptions<AzureDevOpsOptions> devOpsOptions,
        ILogger<MainWindowViewModel> logger)
    {
        _catalogRepository = catalogRepository;
        _logRepository = logRepository;
        _configurationRepository = configurationRepository;
        _devOpsOptions = devOpsOptions.Value;
        _logger = logger;

        _baseUrl = _devOpsOptions.BaseUrl;
        _collection = _devOpsOptions.Collection;
        _apiVersion = _devOpsOptions.ApiVersion;
    }

    // UI State for Dashboard
    private string _currentTab = "0";
    public string CurrentTab
    {
        get => _currentTab;
        set { _currentTab = value; OnPropertyChanged(); }
    }

    public string MachineUserName { get; } = Environment.UserName;
    public string OSVersion { get; } = Environment.OSVersion.ToString();

    // Notification State
    private bool _isNotificationVisible;
    private string _notificationTitle = string.Empty;
    private string _notificationMessage = string.Empty;
    private bool _isNotificationError;

    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        set { _isNotificationVisible = value; OnPropertyChanged(); }
    }

    public string NotificationTitle
    {
        get => _notificationTitle;
        set { _notificationTitle = value; OnPropertyChanged(); }
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        set { _notificationMessage = value; OnPropertyChanged(); }
    }

    public bool IsNotificationError
    {
        get => _isNotificationError;
        set { _isNotificationError = value; OnPropertyChanged(); }
    }

    private void ShowNotification(string title, string message, bool isError = false)
    {
        NotificationTitle = title;
        NotificationMessage = message;
        IsNotificationError = isError;
        IsNotificationVisible = true;

        // Auto hide after 3.5 seconds
        Task.Run(async () =>
        {
            await Task.Delay(3500);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsNotificationVisible = false);
        });
    }

    public void SetTab(string tabIndexStr)
    {
        CurrentTab = tabIndexStr;
    }

    public async Task LoadProjectsAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Cargando proyectos desde la base de datos…";
        try
        {
            var projects = await Task.Run(() => _catalogRepository.GetEnabledProjectsAsync(ct), ct);
            Projects.Clear();
            foreach (var p in projects)
                Projects.Add(ProjectRowViewModel.FromEntity(p));

            IsDbConnected = true;
            StatusMessage = $"Se cargaron {projects.Count} proyecto(s).";
            _logger.LogInformation("Loaded {Count} projects from catalog.", projects.Count);
        }
        catch (Exception ex)
        {
            IsDbConnected = false;
            StatusMessage = $"Error al cargar proyectos: {ex.Message}";
            _logger.LogError(ex, "Failed to load projects from catalog.");
            ShowNotification("Error de BD", ex.Message, true);
        }
        finally { IsBusy = false; }
    }

    public async Task SaveCatalogChangesAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Guardando cambios del catálogo…";
        try
        {
            int saved = 0;
            // Execute all saves on background thread
            await Task.Run(async () => 
            {
                foreach (var row in Projects)
                {
                    await _catalogRepository.MarkProjectAccessStatusAsync(row.ProjectId, row.AccessStatus, ct);
                    saved++;
                }
            }, ct);
            
            StatusMessage = $"{saved} proyecto(s) actualizados correctamente.";
            _logger.LogInformation("Saved {Count} catalog changes.", saved);
            ShowNotification("Catálogo Guardado", $"{saved} proyectos actualizados.", false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
            _logger.LogError(ex, "Failed to save catalog changes.");
            ShowNotification("Error al guardar", ex.Message, true);
        }
        finally { IsBusy = false; }
    }

    public async Task TestTfsConnectionAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"Probando conexión a {BaseUrl}…";
        try
        {
                        var handler = new System.Net.Http.HttpClientHandler();
            if (UseDefaultCredentials)
            {
                handler.UseDefaultCredentials = true;
            }
            else
            {
                handler.Credentials = new System.Net.NetworkCredential(AuthUsername, AuthPassword, AuthDomain);
            }
            using var httpClient = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var response = await Task.Run(() => httpClient.GetAsync(BaseUrl.TrimEnd('/') + "/" + Collection, ct), ct);
            if (response.IsSuccessStatusCode)
            {
                StatusMessage = $"Conexión exitosa. HTTP {(int)response.StatusCode}.";
                ShowNotification("Conexión Exitosa", $"Se alcanzó el servidor TFS (HTTP {(int)response.StatusCode}).", false);
            }
            else
            {
                StatusMessage = $"Servidor responde con HTTP {(int)response.StatusCode}.";
                ShowNotification("Error de Conexión", $"Servidor responde con HTTP {(int)response.StatusCode}.", true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo conectar: {ex.Message}";
            _logger.LogWarning(ex, "TFS connectivity test failed.");
            ShowNotification("Error de Conexión", ex.Message, true);
        }
        finally { IsBusy = false; }
    }

    public async Task SaveConfigurationAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Guardando configuración…";
        try
        {
                        var config = new AzureDevOpsOptions
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
            await Task.Run(() => _configurationRepository.SetConfigurationAsync("SystemConfig", configJson, ct), ct);
            StatusMessage = "Configuración guardada en la base de datos.";
            _logger.LogInformation("System configuration saved to DB.");
            ShowNotification("Configuración Guardada", "Los cambios se guardaron con éxito en la base de datos.", false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar configuración: {ex.Message}";
            _logger.LogError(ex, "Failed to save system configuration.");
            ShowNotification("Error al guardar", ex.Message, true);
        }
        finally { IsBusy = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
}
