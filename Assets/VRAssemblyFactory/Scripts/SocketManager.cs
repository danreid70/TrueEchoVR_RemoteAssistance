using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRAssemblyFactory
{
    public class SocketManager : MonoBehaviour
    {
        [System.Serializable]
        public class SockerPlugPair
        {
            public GameObject socket;

            [SerializeField] public List<Plug> plugs;

            // A constructor makes adding data in one line possible
            public SockerPlugPair(GameObject newSocket, Plug newPlug)
            {
                socket = newSocket;
                plugs = new List<Plug> { newPlug };
            }
        }

        [System.Serializable]
        public class Plug
        {
            public GameObject plug;
            public Vector3 attachRotationOffset;

            // A constructor makes adding data in one line possible
            public Plug(GameObject newPlug)
            {
                plug = newPlug;
                attachRotationOffset = Vector3.zero;
            }
        }

        public List<SockerPlugPair> socketPlugPairs = new List<SockerPlugPair>();

        private void OnValidate()
        {
            foreach (var socketPair in socketPlugPairs)
            {
                //Update socket filter tags to include tags for all plugs
                socketPair.socket.GetComponent<SocketTagFilter>().targetTags.Clear();
                foreach (Plug plugElement in socketPair.plugs)
                {
                    if (plugElement.plug.GetComponent<XRGrabInteractable>())
                    {
                        SocketTagFilter socketTagFilter = socketPair.socket.GetComponent<SocketTagFilter>();
                        if (socketTagFilter && !socketTagFilter.targetTags.Contains(plugElement.plug.tag))
                            socketTagFilter.targetTags.Add(plugElement.plug.tag);
                    }
                    else Debug.LogWarning(plugElement.plug.name + " is not a valid plug");
                }
            }
        }
    }
}