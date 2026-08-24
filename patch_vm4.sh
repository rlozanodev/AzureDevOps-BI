#!/bin/bash
sed -i '/public string Token/i \
    private bool _useDefaultCredentials = true;\
    public bool UseDefaultCredentials { get => _useDefaultCredentials; set { _useDefaultCredentials = value; OnPropertyChanged(); } }\
    private string _authDomain = string.Empty;\
    public string AuthDomain { get => _authDomain; set { _authDomain = value; OnPropertyChanged(); } }\
    private string _authUsername = string.Empty;\
    public string AuthUsername { get => _authUsername; set { _authUsername = value; OnPropertyChanged(); } }\
    private string _authPassword = string.Empty;\
    public string AuthPassword { get => _authPassword; set { _authPassword = value; OnPropertyChanged(); } }\
' src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
