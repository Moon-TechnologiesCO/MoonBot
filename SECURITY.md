# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Use GitHub's private vulnerability reporting: open the repository's **Security** tab → **Report a vulnerability**. Include what you found, how to reproduce it and the version you tested.

We aim to acknowledge reports within 7 days.

## Supported versions

MoonBot is in early development. Only the latest commit on `main` receives security fixes.

## Security principles

- **No secrets in Git.** API keys live in the OS secret store (Windows Credential Manager), never in files.
- **Keys only over HTTPS** (or to `localhost`). MoonBot refuses to send an API key in clear text over the network.
- **Privacy by default.** No telemetry. Logs never contain message text or secrets.
- **Fail closed.** Future agent tools will require explicit permission; anything not allowed is denied.
- **Few dependencies.** Every package is reviewed for license and need ([docs/dependencies.md](docs/dependencies.md)).
