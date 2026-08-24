#!/bin/bash
sed -i 's/private readonly ICatalogRepository _catalogRepository;/private readonly ICatalogRepository _catalogRepository;\n    private readonly ILogRepository _logRepository;/' src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
