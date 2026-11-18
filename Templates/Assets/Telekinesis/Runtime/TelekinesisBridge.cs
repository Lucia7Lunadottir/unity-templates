using System;
using UnityEngine;

// Мост между телекинезом и игрой: пакет ничего не знает про сумку, лут и сохранения, игра подключает их здесь
public static class TelekinesisBridge
{
    // Удержание E: положить предмет под прицелом в сумку, true - положили
    public static event Func<Collider, bool> takeHandler;

    // У предмета нет Rigidbody: выдать его (например, скопировать с префаба выпавшего предмета), null - нечего подхватывать
    public static event Func<Collider, Rigidbody> grabFallback;

    // Предмет отпущен или брошен: перепривязать его запись сохранения к чанку, где он оказался
    public static event Action<GameObject> objectDropped;

    internal static bool TryTake(Collider collider)
    {
        bool taken = false;
        if (takeHandler == null) return false;
        foreach (Func<Collider, bool> handler in takeHandler.GetInvocationList())
            taken |= handler(collider);
        return taken;
    }

    internal static Rigidbody CreateRigidbody(Collider collider)
    {
        if (grabFallback == null) return null;
        foreach (Func<Collider, Rigidbody> handler in grabFallback.GetInvocationList())
        {
            Rigidbody rb = handler(collider);
            if (rb != null) return rb;
        }
        return null;
    }

    internal static void RaiseDropped(GameObject go) => objectDropped?.Invoke(go);
}
