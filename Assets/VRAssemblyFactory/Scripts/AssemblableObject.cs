using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRAssemblyFactory
{
    public class AssemblableObject : MonoBehaviour
    {
        [Header("Prefab to spawn when all parts are socketed")] [Tooltip("Prefab to spawn when all parts are socketed")]
        public GameObject onCompletionPrefab;

        [Tooltip("Add rigidbody & colliders to completed object?")]
        public bool addRigidbodyAndColliders;

        [Header("FX to spawn when all parts are socketed")] [Tooltip("Particle Effects to play")]
        public GameObject completedFX;

        [Tooltip("Audio Clip to play")] public AudioClip completedAudioClip;

        XRGrabInteractable[] grabInteractables;
        XRSocketInteractor[] socketInteractors;

        bool isAssembled = false;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            grabInteractables = GetComponentsInChildren<XRGrabInteractable>();
            socketInteractors = GetComponentsInChildren<XRSocketInteractor>();
            foreach (XRSocketInteractor socketInteractor in socketInteractors)
            {
                socketInteractor.selectEntered.AddListener(CheckIfAllSocketed);
            }
        }

        void CheckIfAllSocketed(SelectEnterEventArgs args)
        {
            //Next frame to allow disabling socketActive by SocketEvents
            StartCoroutine(CheckIfAllSocketedNextFrame());
        }

        IEnumerator CheckIfAllSocketedNextFrame()
        {
            //Wait two frames for socketActive to be disabled in socket events script
            yield return null;
            yield return null;
            foreach (XRSocketInteractor socketInteractor in socketInteractors)
            {
                if (socketInteractor.socketActive)
                {
                    yield break;
                }
            }

            isAssembled = true;
            StartCoroutine(SpawnCompletedObject());
        }

        //Spawn the completed object when the last part is released
        IEnumerator SpawnCompletedObject()
        {
            bool isReleased = false;
            while (!isReleased)
            {
                isReleased = true;
                foreach (XRGrabInteractable grabInteractable in grabInteractables)
                {
                    if (grabInteractable.firstInteractorSelecting != null)
                    {
                        isReleased = false;
                        break;
                    }
                }

                yield return null;
            }

            GameObject completedOb;
            if (onCompletionPrefab)
            {
                completedOb = Instantiate(onCompletionPrefab, grabInteractables[0].transform.position + Vector3.up,
                    Quaternion.identity);
                if (addRigidbodyAndColliders)
                {
                    completedOb.AddComponent<Rigidbody>().AddForce(Vector3.up * 2, ForceMode.VelocityChange);
                    MeshFilter[] meshFilters = completedOb.GetComponentsInChildren<MeshFilter>();
                    foreach (MeshFilter meshFilter in meshFilters)
                    {
                        MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                        meshCollider.convex = true;
                        meshCollider.sharedMesh = meshFilter.sharedMesh;
                    }
                }

                Destroy(this.gameObject);
            }

            if (completedFX || completedAudioClip)
            {
                completedOb = new GameObject(this.gameObject.name + "_CompletedFX");
                completedOb.transform.position = grabInteractables[0].transform.position;
                if (completedFX)
                    Instantiate(completedFX, completedOb.transform.position, completedOb.transform.rotation);
                if (completedAudioClip)
                    completedOb.AddComponent<AudioSource>().PlayOneShot(completedAudioClip);
            }


        }
    }
}