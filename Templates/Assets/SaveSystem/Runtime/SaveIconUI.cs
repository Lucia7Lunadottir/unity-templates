using PG.MenuManagement;
using UnityEngine;

public class SaveIconUI : MonoBehaviour
{
    [SerializeField] private SaveManager _saveManager;
    [SerializeField] private UIShowHide _saveIcon;

    private void OnEnable()
    {
        _saveManager.saveStarted += ShowIcon;
        _saveManager.saveCompleted += HideIcon;
    }

    private void OnDisable()
    {
        _saveManager.saveStarted -= ShowIcon;
        _saveManager.saveCompleted -= HideIcon;
    }

    private void ShowIcon()
    {
        if (_saveIcon != null) _saveIcon.Show();
    }

    private void HideIcon()
    {
        if (_saveIcon != null) _saveIcon.Hide();
    }
}