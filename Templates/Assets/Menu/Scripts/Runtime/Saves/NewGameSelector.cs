using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace PG.MenuManagement
{
    // New game panel: starts the first scene; startedFromNewGame tells gameplay code it is a fresh start
    public class NewGameSelector : MonoBehaviour
    {
        public static bool startedFromNewGame;

        [FormerlySerializedAs("_transitionSceneManager")]
        [SerializeField] private MenuSceneManager _sceneManager;
        [SerializeField] private string _sceneName = "Tutorial";

        public void LoadTutorialScene(bool value)
        {
            startedFromNewGame = value;
            if (_sceneManager != null) _sceneManager.LoadScene(_sceneName, LoadSceneMode.Single);
            else SceneManager.LoadScene(_sceneName);
        }

        public void ChangeTutorialMode(bool value)
        {
            startedFromNewGame = value;
        }
    }
}
