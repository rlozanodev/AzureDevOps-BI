import os
import re

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'r') as f:
    content = f.read()

# Match each tab
dashboard_match = re.search(r'(<!-- TAB 0: DASHBOARD -->\s*<Grid IsVisible="\{Binding CurrentTab[^>]+>\s*)(.*?)(?=\s*<!-- TAB 1: MAPPING)', content, re.DOTALL)
mapping_match = re.search(r'(<!-- TAB 1: MAPPING.*?<Grid IsVisible="\{Binding CurrentTab[^>]+>\s*)(.*?)(?=\s*<!-- TAB 2: CONFIGURACION)', content, re.DOTALL)
config_match = re.search(r'(<!-- TAB 2: CONFIGURACION -->\s*<Grid IsVisible="\{Binding CurrentTab[^>]+>\s*)(.*?)(?=\s*<!-- TAB 3: LOGS)', content, re.DOTALL)
logs_match = re.search(r'(<!-- TAB 3: LOGS -->\s*<Grid IsVisible="\{Binding CurrentTab[^>]+>\s*)(.*?)(?=\s*</Grid>\s*</ContentControl>)', content, re.DOTALL)

def create_user_control(name, inner_xaml, code_behind_methods):
    xaml = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:AzureDevOps.DesktopManager.ViewModels"
             x:DataType="vm:MainWindowViewModel"
             x:CompileBindings="False"
             x:Class="AzureDevOps.DesktopManager.Views.Tabs.{name}">
    {inner_xaml}
</UserControl>"""
    
    with open(f'src/AzureDevOps.DesktopManager/Views/Tabs/{name}.axaml', 'w') as f:
        f.write(xaml)
        
    cs = f"""using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class {name} : UserControl
{{
    public {name}()
    {{
        InitializeComponent();
    }}
    
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

{code_behind_methods}
}}
"""
    with open(f'src/AzureDevOps.DesktopManager/Views/Tabs/{name}.axaml.cs', 'w') as f:
        f.write(cs)

# Dashboard
dashboard_inner = dashboard_match.group(2).strip()
# Remove the closing </Grid> of the tab
dashboard_inner = dashboard_inner.rsplit('</Grid>', 1)[0].strip()
dashboard_methods = """
    private async void OnGuardarCambiosClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveCatalogChangesAsync();
    }
"""
create_user_control('DashboardTab', dashboard_inner, dashboard_methods)

# Mapping
mapping_inner = mapping_match.group(2).strip()
mapping_inner = mapping_inner.rsplit('</Grid>', 1)[0].strip()
create_user_control('MappingTab', mapping_inner, "")

# Config
config_inner = config_match.group(2).strip()
config_inner = config_inner.rsplit('</Grid>', 1)[0].strip()
config_methods = """
    private async void OnTestTfsConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.TestTfsConnectionAsync();
    }

    private async void OnGuardarConfiguracionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveConfigurationAsync();
    }
"""
create_user_control('ConfigTab', config_inner, config_methods)

# Logs
logs_inner = logs_match.group(2).strip()
logs_inner = logs_inner.rsplit('</Grid>', 1)[0].strip()
logs_methods = """
    private async void OnPrevLogDateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.PrevLogDateAsync();
    }

    private async void OnNextLogDateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.NextLogDateAsync();
    }
"""
create_user_control('LogsTab', logs_inner, logs_methods)

# Update MainWindow.axaml
# First add the namespace xmlns:tabs="using:AzureDevOps.DesktopManager.Views.Tabs"
content = content.replace('xmlns:vm="using:AzureDevOps.DesktopManager.ViewModels"', 'xmlns:vm="using:AzureDevOps.DesktopManager.ViewModels"\n        xmlns:tabs="using:AzureDevOps.DesktopManager.Views.Tabs"')

# Replace the tabs content with the UserControls
new_tabs = """                        <!-- TAB 0: DASHBOARD -->
                        <Grid IsVisible="{Binding CurrentTab, Mode=OneWay, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=0}">
                            <tabs:DashboardTab />
                        </Grid>

                        <!-- TAB 1: MAPPING -->
                        <Grid IsVisible="{Binding CurrentTab, Mode=OneWay, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=1}">
                            <tabs:MappingTab />
                        </Grid>

                        <!-- TAB 2: CONFIGURACION -->
                        <Grid IsVisible="{Binding CurrentTab, Mode=OneWay, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=2}">
                            <tabs:ConfigTab />
                        </Grid>

                        <!-- TAB 3: LOGS -->
                        <Grid IsVisible="{Binding CurrentTab, Mode=OneWay, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=3}">
                            <tabs:LogsTab />
                        </Grid>"""

content = content[:dashboard_match.start()] + new_tabs + '\n                    ' + content[logs_match.end():]

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'w') as f:
    f.write(content)

