using PG.CraftSystem;
using PG.InteractSystem;
using PG.SaveSystem;
using UnityEngine;
using UnityEngine.Events;

// Чертёж в доме: взаимодействие открывает рецепт в Build Mode (BuildCatalog.foundInWorld), лист исчезает навсегда
public class BlueprintPickup : MonoBehaviour, IInteractable
{
    private const string PLAYER_TAG = "Player";

    [SerializeField] private CraftRecipe _recipe;
    [SerializeField] private LocalText _title = new LocalText("blueprint-found-title", "Новый чертёж");
    [SerializeField] private LocalText _body = new LocalText("blueprint-found-body", "{0} - теперь можно построить в режиме строительства.");

    public UnityEvent OnPicked;

    public event System.Action interacted;


    
    private void Start()
    {
        Load();
    }

    public void OnInteract()
    {
        if (_recipe == null) return;
        BlueprintUnlocks.Unlock(_recipe);
        var name = _recipe.resultItem != null ? _recipe.resultItem.nameItem : _recipe.name;
        if (PG.Localization.LocalizationSystem.instance != null && _recipe.resultItem != null)
            name = PG.Localization.LocalizationSystem.instance.GetLocalizedValue(_recipe.resultItem.nameItem);
        BuildBridge.RaiseNotice(_title, new LocalText(string.Format(_body.Get(), name)));

        interacted?.Invoke();
        OnPicked?.Invoke();
        TriggerInteractManager.instance?.RemoveTrigger(this);
        gameObject.SetActive(false);
    }


    public void Load()
    {
        if (BlueprintUnlocks.Has(_recipe)){
            gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(PLAYER_TAG)) TriggerInteractManager.instance?.AddTrigger(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(PLAYER_TAG)) TriggerInteractManager.instance?.RemoveTrigger(this);
    }
}
