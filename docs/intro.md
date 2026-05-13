---
id: intro
title: Notey
sidebar_position: 1
slug: /
---

Notey is a minimal note capture app for quickly creating structured, linked notes in an Obsidian-compatible vault.

It is Windows-first for tray and hotkey behavior, but the core note, vault, editor, OCR, AI, and pipeline logic is designed to work cross-platform during development.

## What Notey does

- Opens from the tray or hotkey into a compact dark markdown editor.
- Autosaves notes into an Obsidian vault.
- Captures screen snips and inserts Obsidian image embeds.
- Runs configurable typed processing pipelines over screenshots or text.
- Uses OCR before AI analysis when the configured AI model cannot read images.
- Extracts people, topics, projects, tags, Teams meeting context, and action sections.
- Keeps AI organization manual and reviewable so raw user notes remain intact.

## Documentation map

- [Getting started](./getting-started/installation.md)
- [Configuration](./getting-started/configuration.md)
- [Obsidian vault conventions](./features/obsidian-vault.md)
- [Screen snips and pipelines](./features/screen-snips-and-pipelines.md)
- [Diagnostics](./operations/diagnostics.md)
- [Windows publishing](./deployment/windows-publishing.md)
