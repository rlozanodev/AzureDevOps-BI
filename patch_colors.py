import re

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'r') as f:
    content = f.read()

# Replace hardcoded colors with DynamicResources
replacements = {
    'Background="#F9FAFB"': 'Background="{DynamicResource AppBackground}"',
    'Background="#111827"': 'Background="{DynamicResource SidebarBackground}"', # Wait, sidebar is #111827 or #1F2937?
    'Background="#1F2937"': 'Background="{DynamicResource SidebarBackground}"', 
    'Background="White"': 'Background="{DynamicResource CardBackground}"',
    'BorderBrush="#E5E7EB"': 'BorderBrush="{DynamicResource BorderColor}"',
    'Foreground="#111827"': 'Foreground="{DynamicResource PrimaryText}"',
    'Foreground="#374151"': 'Foreground="{DynamicResource PrimaryText}"',
    'Foreground="#6B7280"': 'Foreground="{DynamicResource SecondaryText}"',
    'Foreground="#9CA3AF"': 'Foreground="{DynamicResource SecondaryText}"',
    'Background="#EEF2FF"': 'Background="{DynamicResource CardBackground}"', # or something else
    'Background="#F3F4F6"': 'Background="{DynamicResource AppBackground}"',
    
    # Specific buttons/cards
    'Background="#4F46E5"': 'Background="{DynamicResource ButtonPrimaryBg}"',
    'Foreground="White"': 'Foreground="{DynamicResource ButtonPrimaryText}"',
    
    # Logs
    'Background="#1E1E1E"': 'Background="{DynamicResource LogContainerBg}"',
    'BorderBrush="#333333"': 'BorderBrush="{DynamicResource LogBorderColor}"',
    'Foreground="#F8F8F2"': 'Foreground="{DynamicResource LogText}"',
    'Foreground="#A6E22E"': 'Foreground="{DynamicResource LogTime}"',
    'Foreground="#66D9EF"': 'Foreground="{DynamicResource LogLevel}"',
    'Foreground="#75715E"': 'Foreground="{DynamicResource LogContext}"',
    'Background="#3E1A1A"': 'Background="{DynamicResource LogExceptionBg}"',
    'Foreground="#F92672"': 'Foreground="{DynamicResource LogExceptionText}"',
}

for k, v in replacements.items():
    content = content.replace(k, v)

# Fix Sidebar (It uses #111827)
content = content.replace('Background="{DynamicResource SidebarBackground}"', 'Background="{DynamicResource SidebarBackground}"')

# Also fix Styles in MainWindow.axaml
content = content.replace('<Setter Property="Background" Value="White"/>', '<Setter Property="Background" Value="{DynamicResource CardBackground}"/>')
content = content.replace('<Setter Property="BorderBrush" Value="#E5E7EB"/>', '<Setter Property="BorderBrush" Value="{DynamicResource BorderColor}"/>')

content = content.replace('<Setter Property="Background" Value="#4F46E5"/>', '<Setter Property="Background" Value="{DynamicResource ButtonPrimaryBg}"/>')
content = content.replace('<Setter Property="Foreground" Value="White"/>', '<Setter Property="Foreground" Value="{DynamicResource ButtonPrimaryText}"/>')

content = content.replace('<Setter Property="Background" Value="White"/>', '<Setter Property="Background" Value="{DynamicResource ButtonSecondaryBg}"/>')
content = content.replace('<Setter Property="Foreground" Value="#374151"/>', '<Setter Property="Foreground" Value="{DynamicResource ButtonSecondaryText}"/>')

content = content.replace('<Setter Property="Foreground" Value="#9CA3AF"/>', '<Setter Property="Foreground" Value="{DynamicResource SidebarText}"/>')
content = content.replace('<Setter Property="Background" Value="Transparent"/>', '<Setter Property="Background" Value="Transparent"/>')

# Add Theme Toggle Button next to Profile Bubble
theme_toggle = """
                <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="16" HorizontalAlignment="Right" VerticalAlignment="Center">
                    <!-- Theme Toggle -->
                    <Button Click="OnToggleThemeClick" Background="Transparent" BorderThickness="0" Cursor="Hand" Padding="8">
                        <TextBlock Text="🌓" FontSize="20" VerticalAlignment="Center"/>
                    </Button>
"""
content = content.replace('<Button Grid.Column="2" Background="Transparent" BorderThickness="0"', theme_toggle + '                    <Button Background="Transparent" BorderThickness="0"')
content = content.replace('</Grid>\n        </Border>\n\n        <!-- ================================= BODY ================================= -->', '                </StackPanel>\n            </Grid>\n        </Border>\n\n        <!-- ================================= BODY ================================= -->')

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml', 'w') as f:
    f.write(content)
