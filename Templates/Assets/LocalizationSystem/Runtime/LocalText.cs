using System;
using UnityEngine;

// Текст для инспектора: ключ локализации + запасной текст, если ключа нет или перевода не нашлось
[Serializable]
public class LocalText
{
    public string key;
    public string text;
    public LocalText(string text) => this.text = text;
    public LocalText(string key, string text) { this.key = key; this.text = text; }
    public string Get() => string.IsNullOrEmpty(key) || PG.Localization.LocalizationSystem.instance == null ? text : PG.Localization.LocalizationSystem.instance.GetLocalizedValue(key, text);
}
