# Notey

Notey is a small Windows-first, cross-platform-friendly note capture tool for quickly building an organized Obsidian vault. It lives in the system tray on Windows, opens with a hotkey, and provides a dark-mode markdown editor for fast capture.

The app can save screen snips, process screenshots through configurable typed pipelines, run OCR with Tesseract before AI analysis, and insert AI-generated context without discarding the user’s original notes. It also helps organize notes with Obsidian links for people, topics, projects, tags, screenshots, and meeting context.

## Highlights

- Dark Avalonia desktop UI with a markdown editing experience.
- Markdown shortcuts for bold/italic text plus paste, format, and row-continuation support for pipe tables.
- Windows tray/hotkey workflow with cross-platform development fallbacks.
- Obsidian-compatible markdown notes, wiki links, vault folders, and vault-backed tasks.
- Configurable typed pipelines for screenshots and text organization.
- OCR-first AI processing using OpenAI-compatible providers.
- Teams screenshot normalization for meeting title, participants, and action context.
- Manual AI cleanup that preserves raw notes and stages metadata suggestions for review.

## Documentation

Project documentation lives in [`docs/`](docs/intro.md) and is written as Docusaurus-compatible markdown.

## Build and test

```bash
dotnet build Notey.slnx
dotnet test Notey.slnx --no-build --logger "console;verbosity=minimal"
```

## Windows publish

```powershell
./scripts/publish-windows.ps1
```

The publish output is written to `artifacts/publish/Notey-win-x64` by default.
