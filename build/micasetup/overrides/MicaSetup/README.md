# Source Overlay Notes

This folder is the optional full-source fallback when rendered `micasetup.json` is not enough.

Use it for:

- a `KeepMyData` uninstall prompt
- manifest-driven cleanup execution
- custom installer or uninstaller pages
- installer behavior that must live in source instead of JSON

## Files

- `Program.cs.template`
  Current-user-first setup host template.
- `Program.un.cs.template`
  Current-user-first uninstall host template.
- `Support/CleanupManifestModels.cs.template`
  Optional strongly-typed model for `cleanup-manifest.template.json`.

## Intended flow

1. Copy these templates into the generated MicaSetup source tree.
2. Replace `__TOKEN__` values.
3. Render `templates/cleanup-manifest.template.json` into a concrete `cleanup-manifest.json`.
4. Add the rendered manifest to installer resources or the installed directory.
5. Wire uninstall pages or services to evaluate `keepUserData` and execute the selected profile.

Keep this route current-user-first unless product requirements explicitly change.
Do not add `.UseElevated()` by default for a `%LocalAppData%\Programs\CPAD` install.
