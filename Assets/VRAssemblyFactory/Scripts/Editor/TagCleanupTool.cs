using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace VRAssemblyFactory
{
    public class TagCleanupTool : Editor
    {
        [MenuItem("Tools/VR Assembly Factory/Remove Unused 'Part' Tags")]
        public static void RemoveUnusedPartTags()
        {
            // 1. Map every GameObject to its CURRENT tag name
            GameObject[] allObjects =
                GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Dictionary<GameObject, string> objectTagMap = new Dictionary<GameObject, string>();

            HashSet<string> usedTagsInScene = new HashSet<string>();

            foreach (GameObject obj in allObjects)
            {
                objectTagMap[obj] = obj.tag;
                usedTagsInScene.Add(obj.tag);
            }

            // 2. Identify which tags to keep and which to delete
            SerializedObject tagManager =
                new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            List<string> tagsToKeep = new List<string>();
            List<string> tagsToDelete = new List<string>();

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                string tagName = tagsProp.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrEmpty(tagName)) continue;

                if (tagName.StartsWith("Part"))
                {
                    if (usedTagsInScene.Contains(tagName))
                        tagsToKeep.Add(tagName);
                    else
                        tagsToDelete.Add(tagName);
                }
                else
                {
                    tagsToKeep.Add(tagName); // Keep non-"Part" tags
                }
            }

            if (tagsToDelete.Count == 0)
            {
                EditorUtility.DisplayDialog("Tag Cleanup", "No unused 'Part' tags found.", "OK");
                return;
            }

            // 3. Confirmation Dialog
            string list = string.Join(", ", tagsToDelete.Take(5));
            if (tagsToDelete.Count > 5) list += "... and others";

            if (EditorUtility.DisplayDialog("Confirm Cleanup",
                    $"Delete {tagsToDelete.Count} unused tags?\n\nTags to remove: {list}\n\nWARNING: Only the current scene is checked. Make sure that objects in other scenes or prefabs are not using those tags.",
                    "Clean", "Cancel"))
            {
                // 4. Update TagManager (This causes the index shift)
                tagsProp.ClearArray();
                for (int i = 0; i < tagsToKeep.Count; i++)
                {
                    tagsProp.InsertArrayElementAtIndex(i);
                    tagsProp.GetArrayElementAtIndex(i).stringValue = tagsToKeep[i];
                }

                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();

                // 5. THE CRITICAL STEP: Re-apply tags to GameObjects
                // Since we set them by STRING, Unity will find the new correct index
                Undo.RecordObjects(allObjects, "Re-sync Tags");
                int fixedCount = 0;

                foreach (var kvp in objectTagMap)
                {
                    if (kvp.Key != null)
                    {
                        try
                        {
                            kvp.Key.tag = kvp.Value;
                            fixedCount++;
                        }
                        catch (System.Exception)
                        {
                            // This handles cases where a tag was removed because it wasn't a "Part" 
                            // tag but was somehow missing (though our logic prevents this).
                        }
                    }
                }

                Debug.Log($"[TagCleanup] Removed {tagsToDelete.Count} tags and re-synced {fixedCount} GameObjects.");
            }
        }
    }
}