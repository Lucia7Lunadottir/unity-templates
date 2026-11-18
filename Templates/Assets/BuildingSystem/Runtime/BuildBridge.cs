using System;
using UnityEngine;

// Мост между строительством и игрой: пакет ничего не знает про магию, полёт, телекинез, ввод и достижения, игра подключает их здесь
public static class BuildBridge
{
    // Строить сейчас нельзя (магия, полёт, телекинез, диалог, нет опоры под ногами). Аргумент - трансформ игрока. Любой true блокирует вход
    public static event Func<Transform, bool> enterBlockers;

    // Режим надо закрыть прямо сейчас (взлетела, начался диалог). Любой true закрывает режим
    public static event Func<bool> exitTriggers;

    // Режим включился (true) или выключился (false): игра переключает контекст ввода, пока строим
    public static event Action<object, bool> gameplayInputChanged;

    // Что-то построено; true - по рецепту (чертежу)
    public static event Action<bool> built;

    // Число найденных чертежей изменилось
    public static event Action<int> blueprintsChanged;

    // Показать сообщение игроку (например, «Новый чертёж»)
    public static event Action<LocalText, LocalText> noticeRequested;

    // Эта поверхность тоже подходит для постройки (например, снег), если у префаба включён «Allow Snow Surface»
    public static event Func<Collider, bool> extraSurface;

    internal static bool IsEnterBlocked(Transform player)
    {
        if (enterBlockers == null) return false;
        foreach (Func<Transform, bool> blocker in enterBlockers.GetInvocationList())
            if (blocker(player)) return true;
        return false;
    }

    internal static bool ShouldExit()
    {
        if (exitTriggers == null) return false;
        foreach (Func<bool> trigger in exitTriggers.GetInvocationList())
            if (trigger()) return true;
        return false;
    }

    internal static bool IsExtraSurface(Collider collider)
    {
        if (extraSurface == null) return false;
        foreach (Func<Collider, bool> check in extraSurface.GetInvocationList())
            if (check(collider)) return true;
        return false;
    }

    internal static void RaiseGameplayInput(object owner, bool enable) => gameplayInputChanged?.Invoke(owner, enable);
    internal static void RaiseBuilt(bool byRecipe) => built?.Invoke(byRecipe);
    internal static void RaiseBlueprintsChanged(int count) => blueprintsChanged?.Invoke(count);
    internal static void RaiseNotice(LocalText title, LocalText body) => noticeRequested?.Invoke(title, body);
}
