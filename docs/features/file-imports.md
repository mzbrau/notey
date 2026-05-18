---
id: file-imports
title: File imports
sidebar_position: 5
---

Drag files onto the editor to copy them into the Notey vault and insert markdown references at the drop position.

## Images

Image files are copied into `Images/` and inserted as Obsidian image embeds:

```markdown
![[Images/photo.png]]
```

Supported image extensions are `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.bmp`, and `.svg`.

## Attachments

Non-image files such as PDFs and Word documents are copied into an assets folder and inserted as Obsidian links.

For active drafts, files are staged beside the draft:

```text
Notes/
  Draft/
    2026-05-18-1015-note.md
    2026-05-18-1015-note.assets/
      brief.pdf
```

When the draft is processed, staged attachments move to the final note's sibling assets folder and the note links are rewritten:

```text
Notes/
  roadmap.md
  roadmap.assets/
    brief.pdf
```

```markdown
[[Notes/roadmap.assets/brief.pdf|brief.pdf]]
```

If an existing final note is open, attachments are copied directly into that final note's assets folder.

## Outlook `.msg` files

Outlook `.msg` email exports are converted into markdown with common email metadata, including sender, recipients, subject, sent date, and body text. Non-inline attachments are imported recursively using the same file-type rules. Embedded `.msg` attachments are rendered as nested email sections when present.

A single `.msg` usually stores quoted history as body text rather than structured thread messages, so Notey preserves that quoted history in the imported body instead of reconstructing an artificial thread.

## Cleanup and failure handling

Draft assets are kept with their draft until the draft is processed. If an empty draft is deleted, Notey also removes its matching draft assets folder. Orphaned draft assets folders are pruned on startup.

If an import or attachment promotion fails, Notey leaves the draft and staged files in place instead of writing final-note links that point at missing files.
