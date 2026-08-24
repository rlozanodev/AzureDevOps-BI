import re

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'r') as f:
    content = f.read()

tabs_start = content.find('<!-- TAB 0: DASHBOARD -->')
tabs_end = content.find('</ContentControl>')

if tabs_start == -1 or tabs_end == -1:
    print("Could not find tabs region")
    exit(1)

tabs_region = content[tabs_start:tabs_end]

parts = re.split(r'<!-- TAB \d: [A-Z ]+-->', tabs_region)
# parts[0] is empty space before first tab
tab0 = parts[1].strip()
tab1 = parts[2].strip()
tab2 = parts[3].strip()
tab3 = parts[4].strip()

def extract_inner(grid_str):
    # Find the first > to skip the <Grid ...> tag
    first_gt = grid_str.find('>')
    # Find the last </Grid>
    last_grid = grid_str.rfind('</Grid>')
    return grid_str[first_gt+1:last_grid].strip()

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

dashboard_methods = """
    private async void OnGuardarCambiosClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveCatalogChangesAsync();
    }
"""
create_user_control('DashboardTab', extract_inner(tab0), dashboard_methods)
create_user_control('MappingTab', extract_inner(tab1), "")

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
create_user_control('ConfigTab', extract_inner(tab2), config_methods)

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
create_user_control('LogsTab', extract_inner(tab3), logs_methods)

content = content.replace('xmlns:vm="using:AzureDevOps.DesktopManager.ViewModels"', 'xmlns:vm="using:AzureDevOps.DesktopManager.ViewModels"\n        xmlns:tabs="using:AzureDevOps.DesktopManager.Views.Tabs"')

new_tabs = """                        <!-- TAB 0: DASHBOARD -->
                        <Grid IsVisible="{Binding CurrentTab, Mode=OneWay, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=0}">
                            <tabs:DashboardTab />
                        </Grid>

                        <!-- TAB 1: CATALOGO -->
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
                        </Grid>\n                    """

content = content[:tabs_start] + new_tabs + content[tabs_end:]

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'w') as f:
    f.write(content)

print("Done")
