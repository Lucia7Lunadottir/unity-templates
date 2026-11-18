using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PG.TemplatesPackageManager
{
    public class GitPackageInstaller : EditorWindow
    {
        private static readonly Color Bg = new Color(0.11f, 0.13f, 0.18f, 1f);
        private static readonly Color Green = new Color(0.32f, 0.88f, 0.44f, 1f);
        private static readonly Color Blue = new Color(0.25f, 0.53f, 0.93f, 1f);
        private static readonly Color Gray = new Color(0.75f, 0.75f, 0.9f, 1f);
        private static readonly Color Orange = new Color(0.95f, 0.7f, 0.3f, 1f);

        private GitCatalog catalog;
        private readonly Dictionary<string, PackageInfo> installed = new Dictionary<string, PackageInfo>();
        private readonly HashSet<string> selected = new HashSet<string>();
        private string search = "";
        private string busyText;
        [System.NonSerialized] private ListRequest listRequest;
        [System.NonSerialized] private AddAndRemoveRequest workRequest;

        [MenuItem("Tools/PG/Git Package Installer")]
        [MenuItem("Window/PG/Git Package Installer")]
        [MenuItem("Window/Package Management/Git Package Installer")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<GitPackageInstaller>();
            wnd.titleContent = new GUIContent("Git Installer");
            wnd.minSize = new Vector2(560, 520);
        }

        private void OnEnable()
        {
            busyText = null;
            catalog = GitPackagesContainer.Load();
            RefreshInstalled();
            CreateGUI();
        }

        private void OnDisable() => EditorApplication.update -= Poll;

        private void RefreshInstalled()
        {
            listRequest = Client.List(true);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private void Poll()
        {
            if (listRequest != null && listRequest.IsCompleted)
            {
                installed.Clear();
                if (listRequest.Status == StatusCode.Success)
                    foreach (var p in listRequest.Result) installed[p.name] = p;
                listRequest = null;
                CreateGUI();
            }

            if (workRequest != null && workRequest.IsCompleted)
            {
                if (workRequest.Status == StatusCode.Failure)
                    Debug.LogError(workRequest.Error?.message ?? "Package request failed");
                workRequest = null;
                busyText = null;
                selected.Clear();
                RefreshInstalled();
                CreateGUI();
            }

            if (listRequest == null && workRequest == null) EditorApplication.update -= Poll;
        }

        private bool IsInstalled(string id) => installed.ContainsKey(id);
        private bool IsEmbedded(string id) => installed.TryGetValue(id, out var p) && p.source != PackageSource.Git && p.source != PackageSource.Registry;

        private string StatusOf(GitPackage pkg, out Color color)
        {
            if (!installed.TryGetValue(pkg.packageId, out var info)) { color = Gray; return "Not installed"; }
            if (IsEmbedded(pkg.packageId)) { color = Orange; return $"{info.source} {info.version}"; }
            color = Green;
            return $"Installed {info.version}";
        }

        private void SelectIds(IEnumerable<string> ids)
        {
            selected.Clear();
            foreach (var id in ids) selected.Add(id);
            CreateGUI();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 16; root.style.paddingRight = 16;
            root.style.paddingTop = 16; root.style.paddingBottom = 8;

            var title = new Label("Git Package Installer");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 20;
            title.style.marginBottom = 8;
            root.Add(title);

            var searchField = new ToolbarSearchField { value = search };
            searchField.style.alignSelf = Align.Stretch;
            searchField.style.width = Length.Auto();
            searchField.style.marginLeft = 0; searchField.style.marginRight = 0;
            searchField.style.marginBottom = 8;
            searchField.RegisterValueChangedCallback(e => { search = e.newValue; CreateGUI(); searchField.Focus(); });
            root.Add(searchField);

            var presets = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginBottom = 8, flexShrink = 0 } };
            presets.Add(new Label("Presets:") { style = { marginRight = 8 } });
            foreach (var preset in catalog.presets)
            {
                var p = preset;
                presets.Add(SmallButton(p.name, () => SelectIds(p.packageIds)));
            }
            presets.Add(SmallButton("Select visible", () => SelectIds(Visible().Select(x => x.packageId))));
            presets.Add(SmallButton("Clear", () => SelectIds(new string[0])));
            root.Add(presets);

            var closure = GitPackagesContainer.Resolve(catalog, selected);
            int extra = closure.Count - selected.Count;
            var summary = new Label($"Selected: {selected.Count}" + (extra > 0 ? $" (+{extra} dependencies)" : "")) { style = { marginBottom = 4, flexShrink = 0 } };
            root.Add(summary);

            var scroll = new ScrollView { style = { flexGrow = 1f, flexShrink = 1f, flexBasis = 0, minHeight = 0 } };
            root.Add(scroll);
            string lastGroup = null;
            foreach (var pkg in Visible())
            {
                if (pkg.group != lastGroup)
                {
                    lastGroup = pkg.group;
                    var header = new Label(pkg.group) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 2, color = Blue } };
                    scroll.Add(header);
                }
                scroll.Add(BuildRow(pkg, closure));
            }

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8, flexShrink = 0 } };
            actions.Add(ActionButton("Install selected", Install));
            actions.Add(ActionButton("Update selected", UpdateSelected));
            actions.Add(ActionButton("Remove selected", Remove));
            root.Add(actions);

            if (busyText != null)
                root.Add(new Label(busyText) { style = { marginTop = 4, color = Orange } });
        }

        private IEnumerable<GitPackage> Visible() =>
            catalog.packages.Where(p => string.IsNullOrEmpty(search)
                || p.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0
                || p.packageId.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0);

        private VisualElement BuildRow(GitPackage pkg, List<GitPackage> closure)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 3; row.style.paddingBottom = 3;
            row.style.backgroundColor = Bg;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 0.34f);
            row.style.marginBottom = 2;

            bool isDependencyOnly = !selected.Contains(pkg.packageId) && closure.Any(c => c.packageId == pkg.packageId);
            var toggle = new Toggle { value = selected.Contains(pkg.packageId) || isDependencyOnly };
            toggle.SetEnabled(!isDependencyOnly);
            toggle.style.flexShrink = 0;
            toggle.style.minWidth = 20;
            toggle.style.marginLeft = 8; toggle.style.marginRight = 8;
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) selected.Add(pkg.packageId); else selected.Remove(pkg.packageId);
                CreateGUI();
            });
            row.Add(toggle);

            var info = new VisualElement { style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minWidth = 0, overflow = Overflow.Hidden } };
            info.Add(new Label(pkg.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, color = Color.white } });
            var sub = pkg.dependencies.Count > 0 ? "needs: " + string.Join(", ", pkg.dependencies.Select(DisplayName)) : pkg.description;
            info.Add(new Label(sub) { style = { fontSize = 10, color = Gray, whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } });
            row.Add(info);

            var statusText = StatusOf(pkg, out var color);
            if (isDependencyOnly) statusText += "  (dependency)";
            row.Add(new Label(statusText) { style = { width = 190, flexShrink = 0, color = color, fontSize = 12, unityTextAlign = TextAnchor.MiddleLeft, marginRight = 6 } });

            var open = new Button(() => Application.OpenURL(pkg.url)) { text = "🔗", tooltip = pkg.url };
            open.style.width = 26; open.style.flexShrink = 0; open.style.marginRight = 6;
            row.Add(open);
            return row;
        }

        private string DisplayName(string id) => catalog.packages.FirstOrDefault(p => p.packageId == id)?.name ?? id;

        private static Button SmallButton(string text, System.Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.paddingLeft = 12; btn.style.paddingRight = 12;
            btn.style.paddingTop = 4; btn.style.paddingBottom = 4;
            btn.style.marginLeft = 0; btn.style.marginRight = 6;
            return btn;
        }

        private Button ActionButton(string text, System.Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.flexGrow = 1;
            btn.style.flexBasis = 0;
            btn.style.height = 30;
            btn.style.marginLeft = 3; btn.style.marginRight = 3;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = Blue;
            btn.style.color = Color.white;
            btn.SetEnabled(workRequest == null && selected.Count > 0);
            return btn;
        }

        // Install: selected plus dependencies that are missing
        private void Install()
        {
            var add = GitPackagesContainer.Resolve(catalog, selected).Where(p => !IsInstalled(p.packageId)).Select(p => p.url).ToArray();
            Run(add, null, $"Installing {add.Length} package(s)…");
        }

        // Update: re-resolve the git URL of selected packages that came from git
        private void UpdateSelected()
        {
            var add = GitPackagesContainer.Resolve(catalog, selected)
                .Where(p => !IsInstalled(p.packageId) || installed[p.packageId].source == PackageSource.Git)
                .Select(p => p.url).ToArray();
            Run(add, null, $"Updating {add.Length} package(s)…");
        }

        private void Remove()
        {
            var ids = selected.Where(id => IsInstalled(id) && !IsEmbedded(id)).ToArray();
            if (ids.Length == 0) return;
            if (!EditorUtility.DisplayDialog("Remove packages", "Remove:\n" + string.Join("\n", ids), "Remove", "Cancel")) return;
            Run(null, ids, $"Removing {ids.Length} package(s)…");
        }

        // One request for everything: a single resolve and a single recompile
        private void Run(string[] add, string[] remove, string text)
        {
            if ((add == null || add.Length == 0) && (remove == null || remove.Length == 0)) return;
            busyText = text;
            workRequest = Client.AddAndRemove(add, remove);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            CreateGUI();
        }
    }
}
