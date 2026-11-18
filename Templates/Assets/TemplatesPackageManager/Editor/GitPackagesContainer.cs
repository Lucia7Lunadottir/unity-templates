using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PG.TemplatesPackageManager
{
    public static class GitPackagesContainer
    {
        private const string CatalogFile = "Editor/catalog.json";

        public static GitCatalog Load()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GitPackagesContainer).Assembly);
            var path = info == null ? null : Path.Combine(info.resolvedPath, CatalogFile);
            if (path == null || !File.Exists(path))
            {
                Debug.LogError($"Git Package Installer: {CatalogFile} not found");
                return new GitCatalog();
            }
            return JsonUtility.FromJson<GitCatalog>(File.ReadAllText(path));
        }

        // Selected packages plus all their dependencies, each once, dependencies first
        public static List<GitPackage> Resolve(GitCatalog catalog, IEnumerable<string> ids)
        {
            var byId = new Dictionary<string, GitPackage>();
            foreach (var p in catalog.packages) byId[p.packageId] = p;

            var result = new List<GitPackage>();
            var seen = new HashSet<string>();
            void Visit(string id)
            {
                if (!seen.Add(id)) return;
                if (!byId.TryGetValue(id, out var pkg))
                {
                    Debug.LogWarning($"Git Package Installer: '{id}' is not in the catalog");
                    return;
                }
                foreach (var dep in pkg.dependencies) Visit(dep);
                result.Add(pkg);
            }
            foreach (var id in ids) Visit(id);
            return result;
        }
    }
}
