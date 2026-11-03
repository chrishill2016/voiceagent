# CLAUDE.md

- Follow Chris's app standards: C# (pronounce "csharp"), latest .NET LTS, MudBlazor, dark mode by default, version in footer, xUnit + bUnit tests.
- Source code lives in `/source`, tests in `/tests`, workflows in `/.github/workflows`.
- All work happens on a branch with a PR to `main`. Never commit directly to `main`, never merge PRs.
- No secrets or connection strings in the repo or client code. Use managed identity / DefaultAzureCredential.
