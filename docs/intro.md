---
id: intro
title: Notey
sidebar_position: 1
slug: /
---

Notey is a minimal note capture app for quickly creating structured, linked notes in an Obsidian-compatible vault.

It is Windows-first for tray and hotkey behavior, but the core note, vault, editor, OCR, and AI logic is designed to work cross-platform during development.

## What Notey does

- Opens from the tray or hotkey into a compact dark markdown editor.
- Autosaves capture drafts under `Notes/Draft`.
- Uses inline slash commands such as `/topic`, `/meeting`, `/task`, and dynamic folder commands to route final notes.
- Captures persistent image embeds under `Images` or direct OCR snippets for processing.
- Uses OCR and AI to format the final Obsidian markdown note.
- Appends topic notes under same-day headings and writes tasks to `Notes/tasks.md`.

## Documentation map

- [Getting started](./getting-started/installation.md)
- [Configuration](./getting-started/configuration.md)
- [Obsidian vault conventions](./features/obsidian-vault.md)
- [Screen snips and AI](./features/screen-snips-and-pipelines.md)
- [Diagnostics](./operations/diagnostics.md)
- [Windows publishing](./deployment/windows-publishing.md)
