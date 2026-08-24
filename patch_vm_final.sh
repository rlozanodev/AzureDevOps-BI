#!/bin/bash
sed -i '/public event PropertyChangedEventHandler/,$!b;//!d' src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
cat << 'INNER_EOF' >> src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
    public async Task LoadAvailableLogDatesAsync(CancellationToken ct = default)
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
INNER_EOF
