---
id: obsidian-vault
title: Obsidian vault conventions
sidebar_position: 1
---

Notey writes standard markdown files with Obsidian-compatible wiki links.

## Default folders

```text
Notes/
People/
Topics/
Projects/
Attachments/Snips/
```

## People, topics, projects, and tags

Detected metadata is staged in editable fields before persistence. When accepted and saved, people, topics, and projects are resolved into Obsidian wiki links and matching entity documents are created or reused.

Tags are stored as markdown tags in frontmatter and the generated Notey context block.

## Generated context block

Notey manages a context block inside the note:

```md
<!-- notey-context:start -->
## Context
- People: [[People/Jane Doe|Jane Doe]]
- Topics: [[Topics/Planning|Planning]]
<!-- notey-context:end -->
```

This block is regenerated from editable metadata and should not be manually edited.
