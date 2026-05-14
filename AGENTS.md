# AGENTS.md

## Repository Rules
- Do not modify README files or change user-facing desktop UI language unless explicitly requested; desktop UI text must remain Chinese-only.
- Use Git identity `C4AL <104809382+C4AL@users.noreply.github.com>` for commits, and write commit summaries/bodies in concise Chinese.
- Only commit, push, create releases, or push Git tags when explicitly requested, unless the task clearly requires repository synchronization.
- Before editing or committing, inspect `git status` and relevant diffs.
- Stage and commit only current-task files; if unrelated or ambiguous dirty files may be overwritten, staged, reformatted, or regenerated, stop and ask first.

## PowerShell Chinese Encoding
- Before running PowerShell commands that print or consume Chinese text, force UTF-8:

```powershell
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
chcp.com 65001 | Out-Null
```
