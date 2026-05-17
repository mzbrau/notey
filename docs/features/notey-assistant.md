---
id: notey-assistant
title: Notey Assistant
sidebar_position: 4
---

Notey Assistant uses the configured AI provider to respond to prompts about the current note and task list.

Open the assistant from the toolbar. The assistant panel appears above the bottom status bar, spans the full window width, and can be resized vertically for the current session.

## Safe changes

Assistant requests are stateless: each prompt includes the current note and task list, but not prior assistant turns.

When the AI suggests note or task changes, Notey validates the structured operations before enabling **Apply changes**. Nothing is written automatically. In the first version, note edits are limited to the currently open note, and task changes are applied through Notey's task store instead of direct `tasks.md` edits.

Use **Cancel** to stop an in-flight AI request.
