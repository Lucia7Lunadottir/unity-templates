using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace PG.Localization
{
    public partial class LocalizationSystem : MonoBehaviour
    {
        private Dictionary<string, string> localizedText = new Dictionary<string, string>();
        [SerializeField] private TextAsset[] _localizationFiles; // Array of CSV files
        public string currentLanguage = "English";

        public event System.Action<string> localizationChanged;

        [AutoStaticsCleanup]
        public static LocalizationSystem instance;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);

            localizationChanged += SetLocalizationOnScene;

            LoadLocalization();
        }
        private void OnDestroy()
        {
            localizationChanged -= SetLocalizationOnScene;
        }
        void SetLocalizationOnScene(string language)
        {
            LocalizedGameObject[] localizedGameObjects = FindObjectsByType<LocalizedGameObject>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (var item in localizedGameObjects)
            {
                item.LocalizeGameObject();
            }


            LocalizedImage[] localizedImages = FindObjectsByType<LocalizedImage>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (var item in localizedImages)
            {
                item.LocalizeImage();
            }


            LocalizeText[] localizedTexts = FindObjectsByType<LocalizeText>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (var item in localizedTexts)
            {
                item.Localize();
            }
        }
        public void LoadLocalization()
        {
            localizedText.Clear();

            foreach (TextAsset csvFile in _localizationFiles)
            {
                if (csvFile == null)
                {
#if UNITY_EDITOR
                    Debug.LogError($"One of the localization files was not found.");
#endif
                    continue;
                }

                List<string[]> rows = LocalizationTable.ParseCSV(csvFile.text);
                if (rows.Count == 0) continue;

                string[] headers = rows[0];
                int languageIndex = -1;
                for (int i = 0; i < headers.Length; i++)
                {
                    if (headers[i].Trim() == currentLanguage)
                    {
                        languageIndex = i;
                        break;
                    }
                }

                if (languageIndex == -1)
                {
#if UNITY_EDITOR
                    Debug.LogError($"Language '{currentLanguage}' not found in localization file {csvFile.name}.");
#endif
                    continue;
                }

                for (int r = 1; r < rows.Count; r++)
                {
                    string[] fields = rows[r];
                    string key = fields[0].Trim();
                    if (key.Length == 0) continue;

                    if (languageIndex >= fields.Length) continue;
                    string value = fields[languageIndex];

                    if (!localizedText.ContainsKey(key))
                    {
                        localizedText[key] = value;
                    }
                    else
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"Key '{key}' from file {csvFile.name} already exists. Skipping.");
#endif
                    }
                }
            }
            localizationChanged?.Invoke(currentLanguage);
        }

        public void SetLanguage(string value)
        {
            currentLanguage = value;
            LoadLocalization();
        }

        public string GetLocalizedValue(string key, string defaultValue = null)
        {
            if (localizedText.TryGetValue(key, out string value))
            {
                return value;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"Key '{key}' not found in localization.");
#endif
            return string.IsNullOrWhiteSpace(defaultValue) ? key : defaultValue; // Return the key as a fallback
        }
    }
}
