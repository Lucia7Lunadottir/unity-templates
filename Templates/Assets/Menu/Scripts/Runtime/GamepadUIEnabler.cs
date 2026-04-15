using System;
using UnityEngine;
using UnityEngine.InputSystem;
using PG.MenuManagement;

public class GamepadUIEnabler : MonoBehaviour
{
    [SerializeField] private GameObject[] _gameObjects;
    
    private bool _isGamepadMode = false;

    private void Awake()
    {
        // При старте проверяем, нажат ли геймпад прямо сейчас, иначе по умолчанию ПК-режим
        bool useGamepad = Gamepad.current != null && Gamepad.current.IsActuated();
        _isGamepadMode = useGamepad;
        SetUIActive(useGamepad);
    }

    void Update()
    {
        // 1. Проверяем, есть ли активность на геймпаде в ЭТОМ кадре
        // IsActuated() лучше, так как реагирует и на стики, и на триггеры
        bool gamepadActive = Gamepad.current != null && Gamepad.current.IsActuated();

        // 2. Проверяем активность клавиатуры (любая кнопка) или мыши (клики или движение)
        bool kbmActive = (Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                         (Mouse.current != null && (Mouse.current.leftButton.isPressed || 
                                                    Mouse.current.rightButton.isPressed || 
                                                    Mouse.current.delta.magnitude > 0.1f));

        // 3. МЕНЯЕМ СОСТОЯНИЕ ТОЛЬКО ПРИ НАЛИЧИИ АКТИВНОСТИ
        // Если игрок ничего не трогает, оба active = false, и этот блок просто проигнорируется, 
        // сохраняя текущий UI.
        if (gamepadActive)
        {
            SetGamepadMode(true);
        }
        else if (kbmActive)
        {
            SetGamepadMode(false);
        }
    }

    private void SetGamepadMode(bool useGamepad)
    {
        // Защита от лишних вызовов (переключаем только если режим реально сменился)
        if (_isGamepadMode != useGamepad)
        {
            _isGamepadMode = useGamepad;
            SetUIActive(useGamepad);
        }
    }

    private void SetUIActive(bool state)
    {
        foreach (var obj in _gameObjects)
        {
            if (obj != null)
            {
                if (obj.TryGetComponent(out UIShowHide panel))
                {
                    if (state) panel.Show();
                    else panel.Hide();
                }
                else
                {
                    obj.SetActive(state);
                }
            }
        }
    }
}