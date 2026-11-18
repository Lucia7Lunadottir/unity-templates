using PG.InventoryManagement;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Item), true)]
[CanEditMultipleObjects]
public class ItemEditor : Editor
{
    private Item _item;
    private void OnEnable()
    {
        _item = (Item)target;
    }

    override public void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (_item.icon != null)
        {
            EditorGUILayout.ObjectField(_item.icon, typeof(Sprite), false, GUILayout.Width(150), GUILayout.Height(150));
        }
    }
}
