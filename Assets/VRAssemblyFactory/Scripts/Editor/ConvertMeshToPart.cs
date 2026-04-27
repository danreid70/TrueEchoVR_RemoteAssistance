using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRAssemblyFactory
{
    public class ConvertMeshToPart : MonoBehaviour
    {
        public static string attachablePartPrefabName = "AttachablePart";

        [MenuItem("GameObject/VR Assembly Factory/MakeAssemblable")]
        public static void MakeAssemblable(MenuCommand command)
        {
            GameObject selectedGameObject = command.context as GameObject;
            if (selectedGameObject == null)
            {
                Debug.LogWarning("No selected game object");
                return;
            }

            CreateAssembly(selectedGameObject, false);
        }

        [MenuItem("GameObject/VR Assembly Factory/MakeAssemblable (Box Collider Method)")]
        public static void MakeAssemblableBoxColliderMethod(MenuCommand command)
        {
            GameObject selectedGameObject = command.context as GameObject;
            if (selectedGameObject == null)
            {
                Debug.LogWarning("No selected game object");
                return;
            }

            CreateAssembly(selectedGameObject, true);
        }

        static void CreateAssembly(GameObject selectedGameObject, bool boxColliderMethod)
        {
            MeshFilter[] meshFilters = selectedGameObject.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                Debug.LogWarning("No renderers found");
                return;
            }

            Undo.AddComponent(selectedGameObject,
                typeof(AssemblableObject)); //Register component to Undo list and adds it
            List<AttachablePartCreator> attachablePartCreators = new List<AttachablePartCreator>();
            List<Collider> colliders = new List<Collider>();
            foreach (MeshFilter child in meshFilters)
            {
                // Cache data
                if (child == null) continue;
                Mesh sourceMesh = child.sharedMesh;
                if (sourceMesh == null) continue;
                Vector3 pos = child.transform.position;
                Quaternion rot = child.transform.rotation;
                Vector3 scale = child.transform.localScale;
                string name = child.name;
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                Material material = renderer ? renderer.sharedMaterial : null;


                GameObject attachablePart = PrefabUtility.InstantiatePrefab(
                    Resources.Load<GameObject>(attachablePartPrefabName),
                    child.transform.parent) as GameObject;


                if (attachablePart == null) continue;

                // Setup new part
                attachablePart.transform.position = pos;
                attachablePart.transform.rotation = rot;
                attachablePart.name = name;
                MeshFilter newMeshFilter = attachablePart.GetComponentInChildren<MeshFilter>();
                if (newMeshFilter == null) continue;
                newMeshFilter.sharedMesh = sourceMesh;
                newMeshFilter.transform.localScale = scale;
                if (material)
                    newMeshFilter.GetComponent<MeshRenderer>().sharedMaterial = material;

                if (boxColliderMethod)
                    newMeshFilter.gameObject.AddComponent<BoxCollider>(); // Add to GO, not filter
                MeshCollider meshCollider = newMeshFilter.gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                meshCollider.sharedMesh = sourceMesh;

                //For Checking contact points
                attachablePartCreators.Add(attachablePart.AddComponent<AttachablePartCreator>());
                colliders.Add(newMeshFilter.GetComponent<Collider>()); // Use new collider

                Undo.RegisterCreatedObjectUndo(attachablePart, "Make Assemblable");

                // Destroy old mesh object
                Undo.DestroyObjectImmediate(child.gameObject);
            }


            foreach (AttachablePartCreator attachablePartCreator in attachablePartCreators)
            {
                attachablePartCreator.collidersToCheck = colliders;
                attachablePartCreator.CheckIntersections();
                colliders.Remove(attachablePartCreator
                    .GetComponentInChildren<Collider>()); //Remove current collider as its already checked
                if (boxColliderMethod)
                    DestroyImmediate(attachablePartCreator.GetComponentInChildren<Collider>());
                DestroyImmediate(attachablePartCreator);
            }
        }
    }
}