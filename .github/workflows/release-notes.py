"""Build GitHub release notes from CHANGELOG.md.

Usage: release-notes.py <tag>  (e.g. v0.3.0)
Takes the matching `## [version]` section, falling back to `## [Unreleased]`
(for probe tags), then to a stub.
"""
import re
import sys

tag = sys.argv[1] if len(sys.argv) > 1 else ""
version = tag[1:] if tag.startswith("v") else tag

with open("CHANGELOG.md", encoding="utf-8") as f:
    text = f.read()


def section(header):
    m = re.search(rf"^## \[{re.escape(header)}\][^\n]*\n(.*?)(?=^## \[|\Z)", text, re.S | re.M)
    return m.group(1).strip() if m else ""


body = section(version) or section("Unreleased") or "See CHANGELOG.md."

header = """Simple TUI text editor in the style of MS Edit / nano. Pure System.Console, no third-party libraries, .NET 10.

Requires the .NET 10 runtime (https://dotnet.microsoft.com/download/dotnet/10.0). Unzip and run tui-edit (on Linux chmod +x first).

"""

with open("NOTES.md", "w", encoding="utf-8") as f:
    f.write(header + body + "\n")

try:
    print(header + body)
except UnicodeEncodeError:
    pass  # non-UTF8 console: NOTES.md is still written correctly
