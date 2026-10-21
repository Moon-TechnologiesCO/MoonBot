# MoonBot 🌙

**A small moon that lives on your desktop and chats with the AI of your choice — local-first, private by default.**

MoonBot is an open desktop assistant by **Moon Technologies**. A friendly moon floats in the corner of your screen, shows what it is doing (thinking, writing, sleeping, error) and opens a chat when you click it. It works with **local models through [Ollama](https://ollama.com)** — your messages never leave your PC — or with any **OpenAI-compatible** server.

> **Status:** early development (v0.2). Windows 10/11 first; Linux and macOS are planned.

[Español abajo ↓](#español)

## Features (v0.2)

- Floating, transparent, always-on-top moon that never steals focus
- Moon-phase animations for each state, eyes that follow the cursor, a speech bubble
- Chat with streaming replies and basic Markdown
- Providers: **Ollama** (local, default) and **OpenAI-compatible** (cloud or LM Studio)
- **8 languages**: English (default), Español, Português, Français, Deutsch, Italiano, 中文, 日本語
- API keys stored in **Windows Credential Manager**, never in plain files
- No telemetry

## Quick start

Requirements: Windows 10/11, [.NET SDK 10.0.401+](https://dotnet.microsoft.com/download) and, for local AI, [Ollama](https://ollama.com).

```powershell
ollama pull gemma3:4b          # a small, fast local model
git clone <this repository>
cd MoonBot
dotnet run --project src/MoonBot.App
```

Click the moon to chat. Use the tray icon to open **Settings** or quit.

## Architecture

```
src/
├─ MoonBot.Core/              pure logic: states, chat session, contracts, translations (no I/O)
├─ MoonBot.Infrastructure/    HTTP providers (Ollama, OpenAI-compatible), JSON settings, file log
├─ MoonBot.Platform.Windows/  Windows-only code (Credential Manager via P/Invoke)
└─ MoonBot.App/               Avalonia 12 UI and composition root
tests/                        xUnit tests for each layer
docs/                         decisions, dependencies and licenses
```

Every dependency points **towards Core**; Core knows nobody. See [docs/decisions.md](docs/decisions.md).

## Tests

```powershell
dotnet test MoonBot.slnx
```

## Security

Please report vulnerabilities privately — see [SECURITY.md](SECURITY.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Code: [Apache License 2.0](LICENSE) — free to use, modify and share, including commercially.
The **MoonBot** and **Moon Technologies** names and the MoonBot character are trademarks of Moon Technologies and are not covered by the license (see [NOTICE](NOTICE)).

---

## Español

**MoonBot** es un asistente de escritorio de **Moon Technologies**: una pequeña luna que flota en tu pantalla, muestra lo que está haciendo y abre un chat al hacer clic. Funciona con **modelos locales mediante Ollama** (tus mensajes no salen de tu PC) o con cualquier servidor **compatible con OpenAI**. Las API keys se guardan en el **Administrador de credenciales de Windows**. Sin telemetría.

Para empezar: instala el SDK de .NET 10 y Ollama, ejecuta `ollama pull gemma3:4b` y luego `dotnet run --project src/MoonBot.App`. El idioma se cambia en **Settings → Language**.
