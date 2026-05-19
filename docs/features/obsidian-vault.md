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
  Customers/
  Draft/
  Meetings/
  Projects/
  Topics/
People/
```

First-level folders under `Notes` become dynamic slash commands. For example, `Notes/Customers` enables `/customer`.

The setup wizard can create the fixed `Customers`, `Projects`, and `Topics` headings, plus child folders for entered customers, projects, and topics. Project and topic setup entries create folders only; Notey writes future markdown documents inside those folders.

Imported non-image files are stored in assets folders. Draft imports are staged under `Notes/Draft/<draft-stem>.assets/`; when a draft is processed they are copied beside the final note under `<note-stem>.assets/` and note links are rewritten to the final vault-relative paths.

## Slash commands and routing

Inline commands at the start of draft lines control where final notes are written:

- `/topic Accounts` writes or appends to a topic note.
- `/meeting` writes a date-prefixed meeting note.
- `/task Follow up // tomorrow` appends to `Notes/tasks.md`.
- Dynamic commands such as `/customer Microsoft` route into matching first-level `Notes` folders.

People links are stored under `People`. Project and topic notes are stored inside their matching `Notes/Projects/<project>` or `Notes/Topics/<topic>` folders.

## Tasks

Notey uses `Notes/tasks.md` as the source of truth for the task panel. Existing readable task lines remain supported:

```markdown
- [ ] Send recap (due: 2026-05-20)
```

When Notey creates or edits a task, it adds a stable Obsidian block id and explicit metadata:

```markdown
- [ ] Send recap (due: 2026-05-20) ^notey-task-abc123
- [x] Send recap (due: 2026-05-20) (completed: 2026-05-21) ^notey-task-def456
```

Tasks captured from a note include a link back to that note, and Notey adds a reciprocal backlink in the source note:

```markdown
- [ ] Review launch (due: 2026-05-20) (source: [[Notes/roadmap|roadmap]]) ^notey-task-abc123
```

Completed tasks stay in their original date section for two calendar days based on the date-only `completed` metadata, then move to the Completed section in the Notey panel.
