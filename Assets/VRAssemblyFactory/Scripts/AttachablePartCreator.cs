using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VRAssemblyFactory
{
    [ExecuteInEditMode]
    public class AttachablePartCreator : MonoBehaviour
    {
        [HideInInspector]
        public List<Collider> collidersToCheck = new List<Collider>(); // Colliders to check for intersection with

        private Transform attachTransform;
        private SocketManager socketManager;

        public void CheckIntersections()
        {
            Collider colA = GetComponentInChildren<Collider>(); // Collider of the current GameObject

            if (collidersToCheck == null || collidersToCheck.Count < 2) return;

            Physics.SyncTransforms(); //To compute collisions in editor mode

            // Iterate through the list of colliders
            for (int i = 0; i < collidersToCheck.Count; i++)
            {
                if (collidersToCheck[i] == null || collidersToCheck[i] == colA) continue;
                Collider colB = collidersToCheck[i];

                if (colA != null && colB != null)
                {
                    CheckAndDrawIntersection(colA, colB);
                }
            }
        }

        private void CheckAndDrawIntersection(Collider a, Collider b)
        {
            attachTransform = b.transform.parent.Find("AttachTransform");
            if (attachTransform.gameObject.activeInHierarchy) return; //Already setup for another socket

            // Physics.ComputePenetration returns true if they overlap
            // It also gives us the direction and distance needed to separate them
            bool isOverlapping = Physics.ComputePenetration(
                a, a.transform.position, a.transform.rotation,
                b, b.transform.position, b.transform.rotation,
                out Vector3 direction, out float distance
            );

            if (isOverlapping)
            {
                // We approximate the intersection point by finding the closest point 
                // on B's surface toward A, adjusted by half the penetration depth.
                Transform bParent = b.transform.parent;
                // We use the renderer's center cause the object might be an off centered pivot
                Vector3 centerA = a.GetComponent<Renderer>().bounds.center;
                Vector3 centerB = b.GetComponent<Renderer>().bounds.center;
                Vector3 relativePos = (centerB - centerA);
                // If the dot product is negative, the vectors are pointing in opposite directions
                if (Vector3.Dot(direction, relativePos) < 0)
                {
                    direction = -direction;
                }

                Vector3 contactPoint =
                    a.ClosestPoint(centerB) + (direction * (distance * 0.5f)); //Midpoint of the intersection
                GameObject socket =
                    PrefabUtility.InstantiatePrefab(Resources.Load("Socket"), this.transform) as GameObject;
                socket.transform.position = contactPoint;
                socket.name += "_" + bParent.gameObject.name;
                string targetTag =
                    "Part_" + bParent.gameObject.name + "_" +
                    Random.Range(0, 10000); //randomize tag name to avoid duplicates for same named objects;
                AddTag(targetTag);
                socket.GetComponent<SocketTagFilter>().targetTags.Add(targetTag);
                bParent.tag = targetTag;
                attachTransform.gameObject.SetActive(true);
                attachTransform.transform.position = contactPoint;
                if (socketManager == null) socketManager = gameObject.AddComponent<SocketManager>();
                SocketManager.Plug plug = new SocketManager.Plug(bParent.gameObject);
                socketManager.socketPlugPairs.Add(new SocketManager.SockerPlugPair(socket, plug));
            }
        }

        public static void AddTag(string tagName)
        {
            // 1. Load the TagManager asset
            SerializedObject tagManager =
                new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            // 2. Check if the tag already exists to avoid duplicates
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals(tagName))
                {
                    Debug.Log($"Tag '{tagName}' already exists.");
                    return;
                }
            }

            // 3. Add the new tag
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
            newTag.stringValue = tagName;

            // 4. Apply changes
            tagManager.ApplyModifiedProperties();
        }
    }
}