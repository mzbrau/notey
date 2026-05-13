---
id: configuration
title: Configuration
sidebar_position: 2
---

Notey reads configuration from `appsettings.json` and optional local overrides in `appsettings.Local.json`. Local settings are intentionally ignored by git so API keys and machine-specific paths are not committed.

## Vault paths

The `Notey:Vault` section controls where notes and linked entity pages are written.

```json
{
  "Notey": {
    "Vault": {
      "RootPath": "C:/Users/me/Obsidian/MyVault",
      "NotesPath": "Notes",
      "PeoplePath": "People",
      "TopicsPath": "Topics",
      "ProjectsPath": "Projects",
      "ScreenshotPath": "Attachments/Snips"
    }
  }
}
```

## AI provider

The default AI provider is OpenAI-compatible. Prefer the `NOTEY_AI_API_KEY` environment variable over plaintext configuration.

```json
{
  "Notey": {
    "Ai": {
      "DefaultProviderId": "default",
      "BaseUrl": "https://api.example.com/v1",
      "ApiKeyEnvironmentVariable": "NOTEY_AI_API_KEY",
      "ModelName": "gpt-compatible-model"
    }
  }
}
```

## OCR

The screenshot AI pipelines use OCR first. If `tesseract` is not on `PATH`, configure the executable explicitly.

```json
{
  "Notey": {
    "Ocr": {
      "TesseractExecutablePath": "C:/Program Files/Tesseract-OCR/tesseract.exe",
      "TesseractDataPath": "C:/Program Files/Tesseract-OCR/tessdata",
      "DefaultLanguage": "eng"
    }
  }
}
```

## Pipeline definitions

Pipeline sequences are stored outside code in `pipelines.json`. The app copies `pipelines*.json` next to the executable on build/publish.
