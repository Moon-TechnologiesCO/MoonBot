# Contributing to MoonBot

Thanks for your interest! MoonBot is also a learning project, so clear explanations are as welcome as code.

## Workflow

1. Create a branch from `main`: `feat/short-name`, `fix/short-name` or `docs/short-name`.
2. **Test first.** Write a failing test, make it pass with the smallest change, then refactor.
3. Keep commits small and use [Conventional Commits](https://www.conventionalcommits.org/): `feat(core): …`, `fix(app): …`, `docs: …`.
4. Run `dotnet test MoonBot.slnx` before opening a pull request. CI runs it too.
5. Stage specific files (`git add path/to/file`), not `git add .`, and check `git status` first.

## Rules

- `MoonBot.Core` has no UI, no I/O and no OS-specific code. Everything else depends on Core, never the reverse.
- Never commit secrets, `.env` files, certificates or personal data.
- A new dependency needs a license review in [docs/dependencies.md](docs/dependencies.md).
- Important decisions go in [docs/decisions.md](docs/decisions.md): what, why, and the alternative we rejected.

## License of contributions

MoonBot is licensed under Apache-2.0. By opening a pull request you agree that your contribution is licensed under the same terms (Apache-2.0, section 5). Sign your commits with `git commit -s` to certify you have the right to submit them ([Developer Certificate of Origin](https://developercertificate.org/)).

## Translations

UI text lives in `src/MoonBot.Core/Localization/strings-<code>.json`. English is the reference. A test fails if a language is missing a key or changes a `{0}` placeholder. Native-speaker reviews are very welcome.
