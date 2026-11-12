using UnityEditor;
using UnityEngine;

// Assigns private [SerializeField] fields from builder scripts.
public static class EditorFields
{
    public static void Set(Object target, string field, System.Action<SerializedProperty> assign)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null) throw new System.Exception($"{target.GetType().Name} has no serialized field {field}");
        assign(prop);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetArray(Object target, string field, Object[] values)
    {
        Set(target, field, p =>
        {
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        });
    }
}
