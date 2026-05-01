# MicaSetup Overlay

This directory keeps repo-owned installer resources that `CodexCliPlus.BuildTool` can render into a real MicaSetup stage.

The goal is to keep installer behavior reviewable in source control instead of burying every decision in `src/CodexCliPlus.BuildTool`.

## Layout

- `templates/micasetup.json.template`
  Tokenized MicaSetup configuration kept as reviewable installer metadata.
- `templates/cleanup-manifest.template.json`
  Repo-owned uninstall cleanup plan with explicit `keepUserData` behavior.
- `templates/installer-plan.example.json`
  Example render input that maps the current stage layout into these templates.
- `source-template/*`
  Fixed MicaSetup source tree copied into the installer stage before repo-owned overrides are rendered.
- `overrides/MicaSetup/*`
  Source overlay rendered on top of `source-template` for CodexCliPlus-specific install and uninstall logic.

## Render contract

These templates expect BuildTool to replace `__TOKEN__` values before installer generation.

Recommended minimum token set:

- `__MICA_TEMPLATE_ARCHIVE__`
- `__PAYLOAD_ARCHIVE_PATH__`
- `__INSTALLER_OUTPUT_PATH__`
- `__VERSION__`
- `__PUBLISHER__`
- `__PRODUCT_GUID__`
- `__FAVICON_PATH__`
- `__SETUP_ICON_PATH__`
- `__UNINSTALL_ICON_PATH__`
- `__LICENSE_FILE__`
- `__INSTALL_DIRECTORY__`
- `__DESKTOP_SHORTCUT__`
- `__START_MENU_DIRECTORY__`
- `__QUICK_LAUNCH_SHORTCUT__`

## Installer defaults

The active BuildTool route copies `source-template` into the stage, renders the source overlays, and builds the installer with `dotnet msbuild` in locked restore mode. BuildTool does not query GitHub releases or download MicaSetup packages during packaging.

Current product identifiers are:

- app name: `CodexCliPlus`
- executable: `CodexCliPlus.exe`
- setup prefix: `CodexCliPlus.Setup`
- managed backend asset: `ccp-core.exe`

The desktop app stores its working state under the CodexCliPlus app data root. Secure credential references are DPAPI-protected `*.bin` files under the app `config\secrets` directory.
There is no Windows Credential Manager entry to remove today.

## Cleanup policy

The cleanup manifest separates removable footprint from user-retained state.

Always remove:

- install directory
- uninstaller sidecar files
- shortcuts
- startup registration for `CodexCliPlus`
- update cache
- runtime and backend working directories
- general cache

Preserve when `KeepMyData = true`:

- `config`
- `config\secrets`
- `logs`
- `diagnostics`

Default uninstall runs the full-clean profile with `KeepMyData = false`.

Remove when `KeepMyData = false`:

- all of the above, then prune the CodexCliPlus app data root if empty

## Source overlay usage

Use `overrides/MicaSetup` when the generated installer needs logic that `micasetup.json` cannot express, such as:

- manifest-aware uninstall cleanup
- a `KeepMyData` prompt or command-line switch
- custom install or uninstall pages

Keep upstream compatibility updates manual: replace `source-template` with an audited snapshot, update overlays only where needed, refresh lock files, and run installer packaging tests.
