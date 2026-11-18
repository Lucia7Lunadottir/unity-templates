using System;

// Хуки катсцен, которые реализует игра. Без них CutsceneDirector только прячет и показывает объекты
public static class CutsceneBridge
{
    // Катсцена началась: заблокировать управление игроком. Аргумент - владелец блокировки
    public static Action<object> inputLock;

    // Катсцена закончилась: вернуть управление владельцу блокировки
    public static Action<object> inputUnlock;
}
