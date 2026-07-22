using UnityEngine;

namespace PG.MenuManagement
{
    [CreateAssetMenu(menuName = "PG/Menu/Saves")]
    public class MenuSaveContainer : ScriptableObject
    {
        public string[] savefiles;
    }
}
