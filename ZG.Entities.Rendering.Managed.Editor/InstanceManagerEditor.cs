#if ZG_ASSET_STREAMING
#define INSTANCE_ASSET_STREAMING
#endif

using UnityEditor;
using UnityEngine;

namespace ZG
{

    [CustomPropertyDrawer(typeof(InstanceManagedPrefab))]
    public class InstanceManagerEditor : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float singleLineHeight = EditorGUIUtility.singleLineHeight;
            position.height = singleLineHeight;
            bool isExpanded = property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label);
            if (isExpanded)
            {
                ++EditorGUI.indentLevel;
                
                position.y += singleLineHeight;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("name"));

                position.y += singleLineHeight;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("destroyMessageName"));

                position.y += singleLineHeight;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("destroyMessageValue"));

                position.y += singleLineHeight;

                var gameObjectName = property.FindPropertyRelative("gameObjectName");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(gameObjectName.stringValue);

                prefab = EditorGUI.ObjectField(position, "Prefab", prefab, typeof(GameObject), false) as GameObject;

                gameObjectName.stringValue = prefab == null ? string.Empty : AssetDatabase.GetAssetPath(prefab);

#if !INSTANCE_ASSET_STREAMING
            property.FindPropertyRelative("gameObject").objectReferenceValue = prefab;
#endif

                position.y += singleLineHeight;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("destroyTime"));

                --EditorGUI.indentLevel;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return property.isExpanded ? EditorGUIUtility.singleLineHeight * 6.0f : EditorGUIUtility.singleLineHeight;
        }
    }
}