<div align="center">
  <img src="resources/icon.png" alt="Notey logo" width="96" height="96" />
  <h1>Notey</h1>
  <p><strong>Minimal note capture for structured, linked Obsidian vaults</strong></p>

  [![CI](https://github.com/mzbrau/notey/actions/workflows/ci.yml/badge.svg)](https://github.com/mzbrau/notey/actions/workflows/ci.yml)
  [![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
</div>

---

## 📚 Documentation

Full documentation is available at **[www.notey-notes.com](https://www.notey-notes.com)** — covering installation, configuration, every feature, and deployment.

---

## What is Notey?

Notey is a Windows-first, cross-platform-friendly desktop app that lives in the system tray. Press a hotkey, capture a thought, and Notey routes it into your Obsidian vault as a properly structured markdown note — with wikilinks, tags, headings, and AI formatting — without breaking your flow.

## Features

| Feature | Description |
|---------|-------------|
| ⚡ **Instant capture** | Global hotkey opens a compact dark markdown editor from the system tray |
| 🗂️ **Slash commands** | `/topic`, `/meeting`, `/task`, and dynamic folder commands route notes to the right place |
| 🤖 **AI formatting** | OCR + AI turn raw drafts into structured Obsidian markdown with canonical people references |
| 🔗 **Obsidian-native** | Every note lands in your vault with proper `[[wikilinks]]`, inline tags, and same-day headings |
| 📸 **Screen snips** | Capture screenshots directly into the editor; OCR extracts text automatically |
| 📋 **Smart paste** | Paste Word/Excel/Google Docs tables and get clean GitHub-flavored pipe tables |
| ✅ **Task tracking** | Tasks route to `Notes/tasks.md` with markdown checkboxes, shown in a live vault-wide panel |
| 🌙 **Dark-first editor** | Syntax highlighting, table navigation, and fixed-width font for aligned writing |

## Quick start

**Requirements:** Windows (for tray/hotkey), .NET 10 SDK, Tesseract OCR on `PATH`, an Obsidian vault folder.

```bash
# Build
dotnet build Notey.slnx

# Test
dotnet test Notey.slnx --no-build --logger "console;verbosity=minimal"

# Run (after build)
dotnet run --project src/Notey.App
```

**Install from a release:** Download the latest Windows setup executable from [GitHub Releases](https://github.com/mzbrau/notey/releases) and run it. Installers are packaged with Velopack and target `win-x64`.

**Publish a self-contained Windows build:**

```powershell
./scripts/publish-windows.ps1
# Output → artifacts/publish/Notey-win-x64
```

See [Installation](https://www.notey-notes.com/docs/getting-started/installation) and [Configuration](https://www.notey-notes.com/docs/getting-started/configuration) in the docs for full setup instructions, including AI provider and vault configuration.

## Contributing

Contributions are welcome. Please open an issue to discuss significant changes before submitting a pull request.

1. Fork the repository and create a feature branch.
2. Make your changes and ensure all tests pass: `dotnet test Notey.slnx --no-build`.
3. Open a pull request against `main`.

## License

Distributed under the [Apache 2.0 License](LICENSE).
