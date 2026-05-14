---
id: obsidian-vault
title: Obsidian vault conventions
sidebar_position: 1
---

Notey writes standard markdown files with Obsidian-compatible wiki links.

## Default folders

```text
Images/
Notes/
  Draft/
  Meetings/
People/
```

First-level folders under `Notes` become dynamic slash commands. For example, `Notes/Customers` enables `/customer`.

## Slash commands and routing

Inline commands at the start of draft lines control where final notes are written:

- `/topic Accounts` writes or appends to a topic note.
- `/meeting` writes a date-prefixed meeting note.
- `/task Follow up // tomorrow` appends to `Notes/tasks.md`.
- Dynamic commands such as `/customer Microsoft` route into matching first-level `Notes` folders.

People links are stored under `People`. Topic/project entity links are stored under `Notes/Topics` and `Notes/Projects`.
