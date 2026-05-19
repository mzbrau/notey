---
id: screen-snips-and-ai
title: Screen snips and AI
sidebar_position: 2
---

Screen snips can be saved into the vault and inserted into the note as Obsidian image embeds, or processed immediately with OCR.

## Persistent image snips

The persistent command captures a region, saves it under `Images`, and inserts an Obsidian image embed into the current draft. Embedded vault images are available to OCR during final note processing.

## Direct OCR snips

The direct command captures a temporary region, restores the editor, and processes OCR/AI work in the background so you can keep typing. The temporary image is deleted after processing completes.

When AI finds confident people in the OCR text, Notey creates or reuses People documents, stores the people in note metadata as Obsidian links, and inserts person links into the captured note content. People must meet the configured confidence threshold (`Notey:Ai:PersonConfidenceThreshold`, default `0.85`).

When no confident people are found, Notey appends the OCR text directly to the end of the current note with a blank line separator. AI-generated tags are added only when they meet `Notey:Ai:TagConfidenceThreshold` (default `0.75`) and are one or two words.
