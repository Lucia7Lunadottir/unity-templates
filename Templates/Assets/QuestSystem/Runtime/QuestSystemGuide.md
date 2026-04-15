# Quest System — Руководство по подключению

## Структура файлов

```
Quest System/
├── QuestData.cs            — ScriptableObject, описание квеста
├── QuestContainer.cs       — ScriptableObject, реестр всех квестов (для save/load)
├── QuestManager.cs         — Singleton MonoBehaviour, вся логика + сохранение
├── QuestHUD.cs             — HUD: список "? Название" для активных квестов
├── QuestHUDEntry.cs        — Одна строчка на HUD
├── QuestMenuPanel.cs       — Журнал квестов (меню)
├── QuestEntryUI.cs         — Строка в списке журнала
├── QuestObjectiveUI.cs     — Строка задачи в детальном виде
└── Nodes/
    ├── GiveQuestNode.cs               — Выдать квест
    ├── CompleteQuestObjectiveNode.cs  — Засчитать прогресс задачи
    ├── CompleteQuestNode.cs           — Принудительно завершить квест
    └── WaitQuestCompleteNode.cs       — Ждать завершения квеста
```

---

## Шаг 1 — Создание данных квеста

1. **Создать QuestContainer** (один на игру):
   - `Assets → Create → PG → Quest → Quest Container`
   - Назвать `AllQuestsContainer`

2. **Создать QuestData** для каждого квеста:
   - `Assets → Create → PG → Quest → Quest`
   - Заполнить:
     - `Quest ID` — **уникальная строка**, например `"quest_find_wood"` (используется для сохранения!)
     - `Title` — название квеста, `Title Key` — ключ локализации (опционально)
     - `Description` / `Description Key` — аналогично
     - `Icon` — иконка (опционально)
     - `Objectives` — список задач. Каждая задача: `Description`, `Required Count` (сколько раз надо выполнить)

3. **Добавить все созданные QuestData в AllQuestsContainer** (поле `Quests`).

---

## Шаг 2 — Настройка сцены

### QuestManager (один в игре)
- Создать GameObject `QuestManager`, добавить компонент `QuestManager`
- Поле `Quest Container` — перетащить `AllQuestsContainer`
- QuestManager помечен `DontDestroyOnLoad` — он переживает загрузку сцен

### QuestHUD
- На HUD-канвасе создать `VerticalLayoutGroup`-объект
- Добавить компонент `QuestHUD`
- `Container` — сам VerticalLayoutGroup
- `Entry Prefab` — префаб со скриптом `QuestHUDEntry`:
  - Image с "?" или иконкой квеста → поле `Quest Icon`
  - TMP_Text с названием квеста → поле `Title Text`

### QuestMenuPanel
- Создать UI-панель журнала квестов (по умолчанию выключена)
- Добавить компонент `QuestMenuPanel`
- **Поля:**
  - `Panel` — корневой GameObject панели
  - `Quest List Container` — Transform для списка квестов (левая колонка)
  - `Entry Prefab` — префаб со скриптом `QuestEntryUI`
  - `First Selectable` — первая кнопка (для геймпада)
  - `No Quest Selected` — заглушка "Выбери квест" (правая колонка, по умолчанию видна)
  - `Quest Detail` — родитель блока деталей (правая колонка)
  - `Detail Title Text`, `Detail Description Text`, `Detail Icon`
  - `Objectives Container` — Transform для списка задач
  - `Objective Prefab` — префаб со скриптом `QuestObjectiveUI`
  - `Toggle Action` — InputAction для открытия/закрытия (например, кнопка Tab или Start)

---

## Шаг 3 — Подключение к Story Graph

В Story Graph используются четыре ноды:

### GiveQuestNode (золотой)
**Назначение:** выдать квест игроку.
**Параметры:**
- `Quest` — перетащить QuestData-ассет

**Когда использовать:** в начале квестовой цепочки, после диалога с NPC.

```
[StartDialogueNode] → [GiveQuestNode: quest_find_wood] → [следующий шаг]
```

---

### CompleteQuestObjectiveNode (зелёный)
**Назначение:** засчитать прогресс одной задачи квеста.
**Параметры:**
- `Quest` — QuestData
- `Objective Index` — номер задачи в списке (0 = первая, 1 = вторая...)
- `Amount` — сколько прогресса добавить (по умолчанию 1)

**Когда использовать:** когда игрок выполнил действие (поговорил, подобрал предмет, убил врага).
Если все задачи выполнены — квест завершается **автоматически**.

```
[WaitFireplaceNode] → [CompleteQuestObjectiveNode: quest_warmup, index=0] → ...
```

---

### CompleteQuestNode (голубой)
**Назначение:** принудительно завершить квест, не проверяя задачи.
**Параметры:**
- `Quest` — QuestData

**Когда использовать:** когда завершение определяется сюжетом, а не счётчиком задач.

```
[StartDialogueNode: "Ты справилась!"] → [CompleteQuestNode: quest_find_wood] → ...
```

---

### WaitQuestCompleteNode (голубой)
**Назначение:** **приостанавливает** граф, пока квест не завершён. После завершения граф продолжается.
**Параметры:**
- `Quest` — QuestData

**Когда использовать:** когда следующий шаг сюжета должен запуститься **именно** в момент завершения квеста.

```
[GiveQuestNode] → [WaitQuestCompleteNode: quest_find_wood] → [StartDialogueNode: "Ты нашла дрова!"]
```

---

## Типичный сценарий

```
Story Graph:

[Start]
  │
  ▼
[StartDialogueNode]         ← NPC просит найти дрова
  │
  ▼
[GiveQuestNode]             ← Квест "Найди дрова" выдан → появляется на HUD
  │
  ▼
[WaitQuestCompleteNode]     ← Граф ждёт...
  │                            (Игрок идёт, кидает дрова в камин)
  │                            (WaitFireplaceNode в другом месте → CompleteQuestObjectiveNode)
  │                            → квест завершается → WaitQuestCompleteNode продолжается
  ▼
[StartDialogueNode]         ← NPC благодарит
  │
  ▼
[End]
```

---

## Важные замечания

- **Quest ID должен быть уникальным** и **не меняться** после начала разработки — он используется для сохранения.
- Все QuestData **обязательно** должны быть добавлены в `QuestContainer`, иначе прогресс не восстановится после загрузки.
- `QuestManager` — синглтон с `DontDestroyOnLoad`. Создавать его только **один раз** (например, на стартовой сцене).
- `CompleteQuestObjectiveNode` и `CompleteQuestNode` **сразу переходят** к следующей ноде — они не останавливают граф.
- `WaitQuestCompleteNode` **останавливает** граф до завершения. Избегай ситуации, где граф ждёт квест, который никогда не завершится.
