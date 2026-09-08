"""Build GitHub release notes from the CHANGELOG.md Unreleased section."""
import re

with open("CHANGELOG.md", encoding="utf-8") as f:
    text = f.read()

m = re.search(r"## \[Unreleased\]\n(.*?)(?=\n## \[|\Z)", text, re.S)
body = m.group(1).strip() if m else "See CHANGELOG.md."

header = """Simple TUI text editor in the style of MS Edit / nano. Pure System.Console, no third-party libraries, .NET 10.

Requires the .NET 10 runtime (https://dotnet.microsoft.com/download/dotnet/10.0). Unzip and run tui-edit (on Linux chmod +x first).

"""

with open("NOTES.md", "w", encoding="utf-8") as f:
    f.write(header + body + "\n")

try:
    print(header + body)
except UnicodeEncodeError:
    pass  # non-UTF8 console: NOTES.md is still written correctly
