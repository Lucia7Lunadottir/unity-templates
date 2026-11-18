using System;
using System.Collections.Generic;

namespace PG.TemplatesPackageManager
{
    [Serializable]
    public class GitPackage
    {
        public string name;
        public string url;
        // UPM package name from package.json ("name": "com.pg.xxx")
        public string packageId;
        public string group;
        public string description;
        // packageIds of packages that must be installed together (git URLs cannot be declared in package.json)
        public List<string> dependencies = new List<string>();
    }

    [Serializable]
    public class GitPreset
    {
        public string name;
        public List<string> packageIds = new List<string>();
    }

    [Serializable]
    public class GitCatalog
    {
        public List<GitPackage> packages = new List<GitPackage>();
        public List<GitPreset> presets = new List<GitPreset>();
    }
}
