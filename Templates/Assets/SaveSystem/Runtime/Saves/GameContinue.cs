using System.IO;
using PG.SaveSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PG.MenuManagement
{
    // Continue button of the selected slot: loads the last saved scene, or the new-game scene for an empty slot
    public class GameContinue : MonoBehaviour
    {
        [SerializeField] private MenuSceneManager _sceneManager;
        [Tooltip("File inside the slot folder that marks a started game; empty = any existing slot counts.")]
        [SerializeField] private string _checkFileExist;
        [SerializeField] private string _tutorialScene;
        [Tooltip("Scene to load when the slot has no last-scene record.")]
        [SerializeField] private string _fallbackScene;

        public void LoadScene()
        {
            bool started = string.IsNullOrEmpty(_checkFileExist)
                ? SaveManager.SlotExists(SaveManager.saveIndex)
                : File.Exists(Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _checkFileExist));

            if (started)
            {
                string lastScene = SaveManager.GetLastSceneName();
                Load(!string.IsNullOrEmpty(lastScene) ? lastScene : _fallbackScene);
            }
            else
            {
                NewGameSelector.startedFromNewGame = true;
                Load(_tutorialScene);
            }
        }

        private void Load(string sceneName)
        {
            if (_sceneManager != null) _sceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            else SceneManager.LoadScene(sceneName);
        }
    }
}
