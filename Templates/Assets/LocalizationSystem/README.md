# Localization System

`LocalizationSystem` loads one or more CSV tables (`TextAsset`) and returns the text for the current language by key.

## Table format

- First row: header. First column is the key, the other columns are language names (`Russian`, `English`, ...). `currentLanguage` must match a header exactly.
- Fields in quotes follow RFC 4180: they may contain commas, line breaks and doubled quotes (`""`), and keep their spaces.
- Fields without quotes: a comma followed by a space is part of the text, any other comma separates columns. Spaces around the field are dropped.
- Blank lines are skipped. An empty cell gives an empty string, a row without the language column is skipped.
- If the same key appears twice, the first one wins and a warning is logged in the Editor.
- A missing key returns the default text passed by the caller, or the key itself.

## Inspector: `[LocalizedKey]`

Put the attribute on a `string` field that holds a key:

```csharp
[LocalizedKey] public string titleKey;
[LocalizedKey("Items")] public string itemNameKey;   // only keys from the table Items.csv
```

The field shows the translation under the key, a language selector for the preview and a key search (the search matches keys and translations). If the key has no translation, the key itself is shown. Double-click the translation to open the key in the Localization Editor.

## Project Settings > Localization

Lists the CSV tables used by the editor tools and the preview language. Stored in `ProjectSettings/LocalizationSettings.asset`. On first use all CSV tables whose header starts with `key` are added.

## Localization Editor (Tools > PG > Localization Editor)

Table view of keys and languages with inline editing, search over keys and translations, the Missing and Duplicates filters, language visibility, adding and removing keys and languages, a detail panel for long texts, Undo/Redo (Ctrl+Z / Ctrl+Y), Save (Ctrl+S) and Search (Ctrl+F). Find usages (button in the detail panel) lists the prefabs, scenes, ScriptableObjects, Timeline and dialogue assets that use the selected key as an exact value (Assets/Plugins is skipped); double-click an entry to open the asset. Saving keeps the file's BOM and line endings and quotes a field only when needed.
