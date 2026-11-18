using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEditorInternal;

[CustomPropertyDrawer(typeof(TagDropdownAttribute))]
public class TagDropdownDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        // Check if the target property is a string
        if (property.propertyType != SerializedPropertyType.String)
        {
            var errorLabel = new Label($"[TagDropdown] works only with string fields ({property.displayName})");
            errorLabel.style.color = UnityEngine.Color.red;
            return errorLabel;
        }

        // Retrieve available tags from InternalEditorUtility
        List<string> tags = new List<string>(InternalEditorUtility.tags);

        // Create DropdownField with current tag list
        var dropdown = new DropdownField(property.displayName, tags, 0);

        // Set current selection value
        string currentValue = property.stringValue;
        if (tags.Contains(currentValue))
        {
            dropdown.value = currentValue;
        }
        else if (tags.Count > 0)
        {
            // Default to Untagged or first available tag if value is empty/invalid
            dropdown.value = tags[0];
            property.stringValue = tags[0];
            property.serializedObject.ApplyModifiedProperties();
        }

        // Register change callback to update SerializedProperty
        dropdown.RegisterValueChangedCallback(evt =>
        {
            property.stringValue = evt.newValue;
            property.serializedObject.ApplyModifiedProperties();
        });

        // Enable automatic binding for UI Toolkit
        dropdown.BindProperty(property);

        return dropdown;
    }
}