---
id: screen-snips-and-pipelines
title: Screen snips and pipelines
sidebar_position: 2
---

Screen snips are saved into the vault and inserted into the note as Obsidian image embeds.

## Save-only snips

The save-only command captures a region and links the image in the current note.

## AI analysis snips

The analysis command captures a region, saves it, and then runs a compatible `ImageData` pipeline. If more than one enabled image pipeline exists, Notey shows a chooser.

The default screenshot pipeline is:

```text
ImageData -> Tesseract OCR -> AI structured extraction -> StructuredNoteData
```

The Teams pipeline adds a Teams meeting normalizer after structured extraction.

## Text organization pipelines

The manual markdown improve command runs enabled `TextData` pipelines. It sends only user-authored note content plus current editable metadata to the pipeline, excluding generated Notey context and prior AI cleanup blocks.

AI cleanup is inserted as a generated block, while metadata suggestions remain editable until accepted.
