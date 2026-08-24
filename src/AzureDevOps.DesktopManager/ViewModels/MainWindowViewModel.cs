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

    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

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
        IOptions<AzureDevOpsOptions> devOpsOptions,
        ILogger<MainWindowViewModel> logger)
    {
        _catalogRepository = catalogRepository;
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
            var projects = await _catalogRepository.GetEnabledProjectsAsync(ct);
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
            foreach (var row in Projects)
            {
                await _catalogRepository.MarkProjectAccessStatusAsync(row.ProjectId, row.AccessStatus, ct);
                saved++;
            }
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
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetAsync(BaseUrl.TrimEnd('/') + "/" + Collection, ct);
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
            var configJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                BaseUrl,
                Collection,
                ApiVersion
            });
            await _configurationRepository.SetConfigurationAsync("SystemConfig", configJson, ct);
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
}
