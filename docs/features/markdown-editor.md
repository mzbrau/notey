---
id: markdown-editor
title: Markdown editor
sidebar_position: 3
---

Notey's editor writes plain markdown and includes shortcuts for common capture-time formatting.

## File drops

Drop files onto the editor to import them into the vault and insert markdown references at the drop position. Images become Obsidian image embeds; PDFs, documents, and other attachments become links to note-specific assets folders. Outlook `.msg` email exports are converted into readable markdown with metadata and imported attachments.

## Inline formatting

- `Ctrl+B` on Windows/Linux or `Command+B` on macOS wraps the selection in `**bold**` markers.
- `Ctrl+I` on Windows/Linux or `Command+I` on macOS wraps the selection in `_italic_` markers.

If no text is selected, the shortcut inserts empty formatting markers and places the cursor between them.

## Tables

When clipboard content contains a table copied from apps such as Microsoft Word, Google Docs, or a spreadsheet, paste converts the table into a GitHub-flavored markdown pipe table instead of inserting HTML. If Google Docs exposes the table as one cell per line instead of rich table data, Notey can infer the table shape for high-confidence 2-8 column layouts and paste a markdown table. Ambiguous newline-only text is left as plain text.

Pasted tables with merged cells are not converted because markdown pipe tables do not support row or column spans.

Use `Ctrl+Alt+T` on Windows/Linux or `Command+Option+T` on macOS to format all markdown tables in the active document. Formatting aligns pipes and separator hyphens, preserves column alignment markers, and skips fenced code blocks. The editor uses a fixed-width font so padded markdown tables align visually while editing. Column padding is capped at 40 characters so very long values do not force every row to become extremely wide; the long cell text is preserved.

Inside a markdown table, `Tab` moves forward through editable cells and `Shift+Tab` moves backward. Navigation skips the separator row. Pressing `Tab` from the final cell adds a new row with the same number of cells, reformats that table, and moves the cursor into the first cell of the new row.
