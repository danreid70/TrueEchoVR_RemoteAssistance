using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace TrueEchoVR
{
    public class QRAnchor : MonoBehaviour
    {
        [SerializeField] private GameObject trackedObjectPrefab;

        // These will be called by the MRUK's Unity Events
        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            Debug.Log($"QR Code added: {trackable.name}");
            Instantiate(trackedObjectPrefab, trackable.transform.position, trackable.transform.rotation);
        }

        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            Debug.Log($"QR Code removed: {trackable.name}");
            Destroy(trackable.gameObject);
        }
    }
}