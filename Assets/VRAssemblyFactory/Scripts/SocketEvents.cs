using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRAssemblyFactory
{
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SocketEvents : MonoBehaviour
    {
        [Tooltip("Audio Clip to play when a object is socketed")]
        public AudioClip onSocketedAudioClip;

        public UnityEvent<SelectEnterEventArgs> onSocketedEvent = new UnityEvent<SelectEnterEventArgs>();
        private XRSocketInteractor xrSocketInteractor;
        private SocketManager socketManager;

        void Start()
        {
            xrSocketInteractor = GetComponent<XRSocketInteractor>();
            xrSocketInteractor.selectEntered.AddListener(LockedSocketedObject);
            xrSocketInteractor.selectEntered.AddListener(OnSocketed);
            socketManager = GetComponentInParent<SocketManager>();
        }

        void OnSocketed(SelectEnterEventArgs args)
        {
            onSocketedEvent.Invoke(args);
            //Put global things to do when an object attaches to a socket here
            if (onSocketedAudioClip)
                gameObject.AddComponent<AudioSource>().PlayOneShot(onSocketedAudioClip);
        }

        void LockedSocketedObject(SelectEnterEventArgs args)
        {
            StartCoroutine(LockSocketedObject());
        }

        IEnumerator LockSocketedObject()
        {
            GameObject ob = xrSocketInteractor.GetOldestInteractableSelected().transform.gameObject;

            yield return null; //wait a frame

            if (!ob.GetComponent<ConfigurableJoint>())
            {
                //Set parent as soon as the object is let go (otherwise parent setting doesn't work)
                xrSocketInteractor.GetOldestInteractableSelected().selectExited.AddListener(args =>
                {
                    ob.transform.SetParent(transform, worldPositionStays: true);
                });
                if (socketManager)
                {
                    foreach (var socketPair in socketManager.socketPlugPairs)
                    {
                        if (socketPair.socket == this.gameObject)
                        {
                            foreach (var plugElement in socketPair.plugs)
                            {
                                if (plugElement.plug == ob)
                                {
                                    transform.localEulerAngles += plugElement.attachRotationOffset;
                                }
                            }
                        }
                    }
                }

                // Destroy(ob.GetComponent<XRGrabInteractable>());
                DisableInterCollision(ob);
                yield return null; //Wait a frame to setup joint
                //Make object move with parent
                SetupJoint(ob);
                //Make socket un-interactable now
                xrSocketInteractor.socketActive = false;
                yield return null; //Have to wait a frame for socket to stop messing the rigidbody in XR toolkit 3.2+
                Rigidbody rb = ob.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.mass = transform.parent.GetComponent<Rigidbody>().mass /
                          10; //Reduce mass to prevent center of mass fighting
            }
        }

        private void SetupJoint(GameObject ob)
        {
            ConfigurableJoint configurableJoint = ob.AddComponent<ConfigurableJoint>();
            configurableJoint.angularXMotion = ConfigurableJointMotion.Locked;
            configurableJoint.angularYMotion = ConfigurableJointMotion.Locked;
            configurableJoint.angularZMotion = ConfigurableJointMotion.Locked;
            configurableJoint.xMotion = ConfigurableJointMotion.Locked;
            configurableJoint.yMotion = ConfigurableJointMotion.Locked;
            configurableJoint.zMotion = ConfigurableJointMotion.Locked;
            configurableJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            configurableJoint.projectionAngle = 0;
            configurableJoint.projectionDistance = 0;
            configurableJoint.connectedBody = transform.parent.GetComponent<Rigidbody>();
        }

        void DisableInterCollision(GameObject ob)
        {
            Collider ColliderA = ob.GetComponentInChildren<Collider>();
            Collider ColliderB = transform.parent.GetComponentInChildren<Collider>();
            Physics.IgnoreCollision(ColliderA, ColliderB);
        }
    }
}