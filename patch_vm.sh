#!/bin/bash
# First, insert ILogRepository in constructor
sed -i 's/IOptions<AzureDevOpsOptions> devOpsOptions,/ILogRepository logRepository,\n        IOptions<AzureDevOpsOptions> devOpsOptions,/' src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
sed -i 's/_catalogRepository = catalogRepository;/_catalogRepository = catalogRepository;\n        _logRepository = logRepository;/' src/AzureDevOps.DesktopManager/ViewModels/MainWindowViewModel.cs
