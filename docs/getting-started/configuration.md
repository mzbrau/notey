---
id: configuration
title: Configuration
sidebar_position: 2
---

Notey reads configuration from `appsettings.json` and optional local overrides in `appsettings.Local.json`. Local settings are intentionally ignored by git so API keys and machine-specific paths are not committed.

## Vault root

The `Notey:Vault` section configures a single vault root. Notey owns `Images`, `Notes`, `Notes/Draft`, and `People` under that root.

```json
{
  "Notey": {
    "Vault": {
      "RootPath": "C:/Users/me/Obsidian/MyVault"
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

Direct screenshot processing and persistent image embeds use OCR. If `tesseract` is not on `PATH`, configure the executable explicitly.

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
