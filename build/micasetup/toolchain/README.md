# Repo-Owned MicaSetup Toolchain

BuildTool checks this directory before using the output cache or downloading MicaSetup tools.

Expected layout:

- `build/bin/7z.exe`
- `build/makemica.exe`
- `build/template/default.7z`
- `micasetup-tools-version.txt`

The binaries are intentionally not committed here. If release engineering decides to vendor them, keep them in this exact layout so the installer build remains bundled-first and only falls back online when the toolchain is absent.
