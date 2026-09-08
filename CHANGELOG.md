# Changelog

DeskMux follows [Semantic Versioning](https://semver.org/). Release notes on GitHub describe changes after the first published version.

## [0.1.0] - 2026-09-08

### Added

- Scoped pane guides, focus outlines, resize highlighting, theme colors and visibility controls.
- Editable shortcuts with recording, search, conflict checking and import/export.
- Undo, drag-to-swap, Alt-drag release, and saved layout presets.
- Monitor reconnect recovery and wake reflow.
- Verified ZIP updates with settings preservation and rollback copies.

### Fixed

- Readable command overlay with short labels and aligned key badges.
- Release checksums are regenerated after executable signing.
- Installer and ZIP contents exclude local sessions, logs and portable user data.

## Initial implementation

### Added

- Named sessions for native Windows application windows.
- Nested panes with horizontal and vertical splits, keyboard navigation, resizing, swapping, zoom, and automatic reflow.
- Floating members, cross-session moves, session picker previews, configurable launchers, and app restoration.
- Local persistence, crash recovery helper, emergency show-all command, and multi-monitor/DPI handling.
- Configurable keyboard prefix and built-in or custom appearance themes.
- Per-user installer definition, portable data mode, release checksums, optional code signing, and user-triggered update checks.
- Separate Coolify-ready landing-page repository for `deskmux.kurtian.dev`, plus GitHub CI, release, and dependency workflows.
