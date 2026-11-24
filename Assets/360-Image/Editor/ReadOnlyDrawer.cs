using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false; // 編集不可
        EditorGUI.PropertyField(position, property, label);
        GUI.enabled = true;  // 他のフィールドは編集可能
    }
}
