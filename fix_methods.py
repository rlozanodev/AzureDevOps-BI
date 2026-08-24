import re

with open('src/AzureDevOps.DesktopManager/Views/Tabs/DashboardTab.axaml.cs', 'r') as f:
    content = f.read()

# Remove method from DashboardTab
content = re.sub(r'    private async void OnGuardarCambiosClick.*?}\n', '', content, flags=re.DOTALL)
with open('src/AzureDevOps.DesktopManager/Views/Tabs/DashboardTab.axaml.cs', 'w') as f:
    f.write(content)


with open('src/AzureDevOps.DesktopManager/Views/Tabs/MappingTab.axaml.cs', 'r') as f:
    content = f.read()

methods = """
    private async void OnGuardarCambiosClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveCatalogChangesAsync();
    }
}"""
content = content.replace('}', methods)

with open('src/AzureDevOps.DesktopManager/Views/Tabs/MappingTab.axaml.cs', 'w') as f:
    f.write(content)
