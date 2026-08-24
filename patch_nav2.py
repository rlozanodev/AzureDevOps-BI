with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'r') as f:
    content = f.read()

old_nav = """    private void OnNavTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.CommandParameter is string tab)
        {
            _vm.CurrentTab = tab;
            Console.WriteLine($"[DEBUG] Changed tab to {tab}");
        }
    }"""

new_nav = """    private async void OnNavTabClick(object? sender, RoutedEventArgs e)
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
    }"""

content = content.replace(old_nav, new_nav)

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'w') as f:
    f.write(content)
