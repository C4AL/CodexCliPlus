# AGENTS.md

## Repository Rules
- Do not modify `README.md` or other README files unless the user explicitly asks for README/documentation changes.
- Keep application display language Chinese-only for all user-facing desktop UI text. Do not introduce English or mixed-language UI strings unless the user explicitly requests it.
- Treat README edits and UI language changes as high-signal product decisions. If a task would require either, stop and confirm with the user first unless the request is explicit.
- When creating commits in this repository, use the repository Git identity `C4AL <104809382+C4AL@users.noreply.github.com>`.
- When the user asks to commit, submit, or otherwise save repository changes, default to committing locally and pushing the current branch to its tracked remote.

## Scope Notes
- "Application display language" refers to desktop app user-facing text, labels, buttons, dialogs, notifications, status text, and settings descriptions.
- Internal code comments, identifiers, logs, tests, and backend/upstream bundled docs should follow the existing conventions in their own files unless the user asks otherwise.
