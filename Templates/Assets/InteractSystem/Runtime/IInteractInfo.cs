using UnityEngine;

namespace PG.InteractSystem
{
    // Что показать в списке выбора взаимодействия. Пустое имя - объект не настроен (покажется имя объекта)
    public interface IInteractInfo
    {
        string interactName { get; }
        Sprite interactIcon { get; }
    }
}
