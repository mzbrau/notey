---
id: screen-snips-and-ai
title: Screen snips and AI
sidebar_position: 2
---

Screen snips can be saved into the vault and inserted into the note as Obsidian image embeds, processed immediately with OCR, or captured with global hotkeys into the clipboard or screenshot editor.

## Persistent image snips

The persistent command captures a region, saves it under `Images`, and inserts an Obsidian image embed into the current draft. Embedded vault images are available to OCR during final note processing.

## Direct OCR snips

The direct command captures a temporary region, restores the editor, and processes OCR/AI work in the background so you can keep typing. The temporary image is deleted after processing completes.

When AI finds confident people in the OCR text, Notey creates or reuses People documents, stores the people in note metadata as Obsidian links, and inserts person links into the captured note content. People must meet the configured confidence threshold (`Notey:Ai:PersonConfidenceThreshold`, default `0.85`).

When no confident people are found, Notey appends the OCR text directly to the end of the current note with a blank line separator. AI-generated tags are added only when they meet `Notey:Ai:TagConfidenceThreshold` (default `0.75`) and are one or two words.

## Global screenshot hotkeys (Windows)

Configurable under Settings → Window (defaults shown):

| Hotkey | Action |
|--------|--------|
| `Ctrl+Alt+3` | Capture the monitor under the cursor and copy the PNG to the clipboard |
| `Ctrl+Alt+4` | Region selector → copy PNG to the clipboard |
| `Ctrl+Alt+5` | Region selector → open the Screenshot Editor |
| `Ctrl+Alt+6` | Window picker → open the Screenshot Editor |

## Screenshot Editor

The editor is a separate non-modal window. Multiple editor windows can be open at once.

Toolbar:

- **Save** — write the flattened image (base + annotations) to disk
- **Copy** — copy the flattened image to the clipboard
- **Add to Notey** — save under vault `Images/` and insert an Obsidian embed into the current draft
- **Select / Arrow / Text / Rectangle / Highlight / Blur** — annotation tools with a shared colour palette
- **Crop** — crop the base image in-place (existing annotations are discarded)

Annotations remain editable (move, resize via handles, delete) until Save, Copy, or Add to Notey flattens them into the exported PNG.
