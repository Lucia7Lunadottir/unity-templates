namespace InputSystemActionPrompts
{
    /// <summary>
    /// Какое устройство использовать для отображения подсказки
    /// </summary>
    public enum PromptDeviceOverride
    {
        AutoActiveDevice, // как раньше – берём активное устройство
        Keyboard,
        Gamepad
        // при желании можно будет добавить Mouse, Touchscreen и т.д.
    }
}
