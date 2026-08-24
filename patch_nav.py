with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'r') as f:
    content = f.read()

nav = """
    private async void OnNavTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.CommandParameter is string tab)
        {
            _vm.CurrentTab = tab;
            Console.WriteLine($"[DEBUG] Changed tab to {tab}");
            
            if (tab == "3")
            {
                await _vm.LoadAvailableLogDatesAsync();
            }
        }
    }
"""

import re
content = re.sub(r'    private void OnNavTabClick\(object\? sender, RoutedEventArgs e\)\s*{\s*if \(sender is RadioButton rb && rb\.CommandParameter is string tab\)\s*{\s*_vm\.CurrentTab = tab;\s*Console\.WriteLine\(\$"{0}"\);\s*}\s*}'.format(r'\[DEBUG\] Changed tab to \{tab\}'), nav, content)

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'w') as f:
    f.write(content)
