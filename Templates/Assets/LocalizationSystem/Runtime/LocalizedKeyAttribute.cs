using System;
using UnityEngine;

namespace PG.Localization
{
    // Marks a string field that holds a localization key. The inspector shows the translation and a key search.
    [AttributeUsage(AttributeTargets.Field)]
    public class LocalizedKeyAttribute : PropertyAttribute
    {
        // Optional: only offer keys from the table with this file name
        public readonly string table;

        public LocalizedKeyAttribute(string table = null)
        {
            this.table = table;
        }
    }
}
