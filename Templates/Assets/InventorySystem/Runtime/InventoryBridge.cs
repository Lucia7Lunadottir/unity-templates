using System;
using PG.InventoryManagement;

// Мост между сумкой и игрой: пакет ничего не знает про смену дня и строительство, игра подключает их здесь
public static class InventoryBridge
{
    // Игровой день для записи выброшенного предмета (нет подписчика или смены дня - 0)
    public static event Func<int> currentDayProvider;

    // Можно ли положить предмет в мир (кнопка «Поставить» в меню предметов): true, если хоть один обработчик разрешил
    public static event Func<Item, bool> canPlaceItem;

    // Положить предмет в мир: игра закрывает меню и включает свой режим размещения (например, строительство)
    public static event Action<Item> placeRequested;

    internal static int CurrentDay()
    {
        if (currentDayProvider == null) return 0;
        foreach (Func<int> provider in currentDayProvider.GetInvocationList())
        {
            int day = provider();
            if (day > 0) return day;
        }
        return 0;
    }

    internal static bool CanPlace(Item item)
    {
        if (canPlaceItem == null) return false;
        foreach (Func<Item, bool> handler in canPlaceItem.GetInvocationList())
            if (handler(item)) return true;
        return false;
    }

    internal static void RequestPlace(Item item) => placeRequested?.Invoke(item);
}
