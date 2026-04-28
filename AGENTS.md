# AGENTS.md

## Repository Rules
- Do not modify `README.md` or other README files unless the user explicitly asks for README/documentation changes.
- Keep application display language Chinese-only for all user-facing desktop UI text. Do not introduce English or mixed-language UI strings unless the user explicitly requests it.
- Treat README edits and UI language changes as high-signal product decisions. If a task would require either, stop and confirm with the user first unless the request is explicit.
- When creating commits in this repository, use the repository Git identity `C4AL <104809382+C4AL@users.noreply.github.com>`.
- When the user asks to commit, submit, or otherwise save repository changes, default to committing locally and pushing the current branch to its tracked remote.
- Future backend upgrades must keep `ccp-core.exe` as the CodexCliPlus managed runtime and packaged asset name. The upstream archive entry `cli-proxy-api.exe` is only an input source name and must not be reintroduced as the project runtime or bundled file name.
- Desktop notification and hint feedback must use the shell notification system: bottom-center auto-dismiss notifications for short success/status feedback, and bottom-right manual-dismiss notifications for failures or items needing user attention. Do not add new ad hoc note text blocks or module-local tip surfaces for notification-style feedback.

## Parallel Agent Usage
- Use parallel agents/concurrent work modes when they can materially improve development efficiency or shorten delivery time. This is not limited to preprocessing or read-only exploration; bounded implementation, test, and verification tasks may also be delegated.
- Keep every delegated task tightly scoped with clear ownership of files or modules, and prefer disjoint write sets. Review and integrate outputs deliberately instead of blindly accepting broad edits.
- Treat correctness as more important than speed. Avoid accidental overwrites, stale-context changes, bulk replacements without review, and edits made without understanding the surrounding project logic or behavior.

## Scope Notes
- "Application display language" refers to desktop app user-facing text, labels, buttons, dialogs, notifications, status text, and settings descriptions.
- Internal code comments, identifiers, logs, tests, and backend/upstream bundled docs should follow the existing conventions in their own files unless the user asks otherwise.
