# Changelog

All notable changes to MoonBot. Format based on [Keep a Changelog](https://keepachangelog.com/); versions follow [SemVer](https://semver.org/).

## [Unreleased]

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
