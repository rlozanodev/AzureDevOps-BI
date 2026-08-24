import re

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'r') as f:
    content = f.read()

# Remove the regions
content = re.sub(r'// ─── Catálogo ───.*?// ─── Logs ───.*?\n', '', content, flags=re.DOTALL)
# The above regex might not catch everything. Let's just do it manually.
lines = content.split('\n')
new_lines = []
skip = False
for line in lines:
    if '// ─── Catálogo ───' in line:
        skip = True
    
    if skip and line.strip() == '}':
        skip = False
        new_lines.append(line)
        continue
        
    if not skip:
        new_lines.append(line)

with open('src/AzureDevOps.DesktopManager/Views/MainWindow.axaml.cs', 'w') as f:
    f.write('\n'.join(new_lines))
