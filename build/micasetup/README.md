# MicaSetup Overlay

This directory keeps repo-owned installer resources that `CPAD.BuildTool` can render into a real MicaSetup stage.

The goal is to keep installer behavior reviewable in source control instead of burying every decision in `src/CPAD.BuildTool`.

## Layout

- `templates/micasetup.json.template`
  Tokenized MicaSetup configuration for the `makemica.exe` route.
- `templates/cleanup-manifest.template.json`
  Repo-owned uninstall cleanup plan with explicit `keepUserData` behavior.
- `templates/installer-plan.example.json`
  Example render input that maps the current stage layout into these templates.
- `overrides/MicaSetup/*`
  Optional source overlay for the full-source MicaSetup route when JSON alone is not enough.

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

## Current-user defaults

The templates are intentionally biased toward current-user install:

- `RequestExecutionLevel = "user"`
- `IsUseInstallPathPreferAppDataLocalPrograms = true`
- install root hint: `%LocalAppData%\Programs\CPAD`
- HKCU startup cleanup only

The current desktop app stores its working state under `%LocalAppData%\CPAD`.
Secure credential references are DPAPI-protected `*.bin` files under `%LocalAppData%\CPAD\config\secrets`.
There is no Windows Credential Manager entry to remove today.

## Cleanup policy

The cleanup manifest separates removable footprint from user-retained state.

Always remove:

- install directory
- uninstaller sidecar files
- current-user shortcuts
- HKCU `Run\CPAD`
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

- all of the above, then prune `%LocalAppData%\CPAD` if empty

## Source overlay usage

Use `overrides/MicaSetup` only when the generated installer needs logic that `micasetup.json` cannot express, such as:

- manifest-aware uninstall cleanup
- a `KeepMyData` prompt or command-line switch
- custom install or uninstall pages

If the stock `makemica.exe` route is enough, only render the JSON templates.
