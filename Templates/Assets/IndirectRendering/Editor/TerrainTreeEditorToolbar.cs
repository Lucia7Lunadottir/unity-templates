#if UNITY_EDITOR && UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace PG.Rendering
{
    // Кнопки на главной панели Unity 6 (там, где Play/Pause): включить/выключить предпросмотр деревьев
    // и пересобрать его. Логика живёт в TerrainTreeEditorPreview - здесь только интерфейс.
    // Появление: правый клик по главной панели -> найти "Indirect" -> поставить галочки.
    [InitializeOnLoad]
    public static class TerrainTreeEditorToolbar
    {
        private const string TogglePath = "Indirect/Деревья Terrain";
        private const string RebuildPath = "Indirect/Пересобрать деревья";

        static TerrainTreeEditorToolbar()
        {
            // Если включили из меню или кнопкой в Scene View - обновляем галочку на панели.
            TerrainTreeEditorPreview.StateChanged -= RefreshToggle;
            TerrainTreeEditorPreview.StateChanged += RefreshToggle;
        }

        private static void RefreshToggle() => MainToolbar.Refresh(TogglePath);

        [MainToolbarElement(TogglePath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreateToggle()
        {
            var content = new MainToolbarContent("Деревья", "Показывать деревья Terrain через Indirect в Editor");
            return new MainToolbarToggle(content, TerrainTreeEditorPreview.IsEnabled,
                value => TerrainTreeEditorPreview.SetEnabled(value));
        }

        [MainToolbarElement(RebuildPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreateRebuildButton()
        {
            var content = new MainToolbarContent("Пересобрать", "Пересобрать деревья Indirect в Editor");
            return new MainToolbarButton(content, () => TerrainTreeEditorPreview.RequestRebuild());
        }
    }
}
#endif
