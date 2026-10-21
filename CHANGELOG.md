# Changelog

All notable changes to MoonBot. Format based on [Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.4.0] - 2026-10-20

### Added
- Voice dictation: hold Right Ctrl, speak, release; text is typed where the cursor is, in any app.
- Local speech recognition with Whisper (Whisper.net, MIT); models downloaded with pinned size and SHA-256.
- Hub → Voice page: switch, model download with progress, language and text destination.
- The moon shows listening waves that follow your voice.
- Issue and pull request templates; CODEOWNERS.

### Fixed
- Picking a skin or accessory crashed the app (settings save/refresh loop).

### Security
- No keyboard hook: MoonBot checks one key's state only, never other keystrokes.
- Unhandled errors are written to the log before the app exits.

## [0.3.0] - 2026-10-20

### Added
- Compact chat window next to the moon; the old panel became the settings Hub.
- `/find` instant file search, file cards with Open / Show in folder, drag-and-drop files.
- Agent with file tools (search, list, create folder, rename, move) inside the user folders.
- 7 skins and 7 accessories with live previews.
- Placement that follows the taskbar edge, a top-center mode, and "remember where I leave it".

### Security
- Every file change asks for approval; unknown tools and bad arguments are rejected; 30 s tool timeout; audit log.
- No delete tool; no overwrite; links and junctions refused; no renaming into executables.

## [0.2.0] - 2026-10-20

### Added
- Chat panel with streaming replies and basic Markdown.
- AI providers: Ollama (local, default) and OpenAI-compatible.
- Settings: language, name, provider, URL, model list, connection test, sleep timer.
- API keys stored in Windows Credential Manager.
- 8 UI languages, English by default.
- Daily log files without message content.
- Redesigned speech bubble; clicking the moon opens the chat.

### Security
- API keys are only sent over HTTPS or to localhost.

## [0.1.0] - 2026-10-20

### Added
- Floating, transparent, always-on-top moon with per-state animations.
- System tray icon and menu.
- `MoonStateMachine` with a table of allowed transitions and a `StateChanged` event.

### Fixed
- `Error` was a dead end; it can now return to `Idle`.
