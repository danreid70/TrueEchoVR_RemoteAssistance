// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Meta.XR.Samples;
using UnityEngine;

namespace Meta.XR.MRUtilityKitSamples.VirtualHome
{
    /// <summary>
    /// Tweaks material properties based on room dimensions to create distance-based gradient effects.
    /// </summary>
    [MetaCodeSample("MRUKSample-VirtualHome")]
    public class RoomBasedMaterialTweak : MonoBehaviour
    {
        private static readonly int DistanceCovered = Shader.PropertyToID("_DistanceCovered");

        private void OnEnable()
        {
            if (MRUK.Instance != null)
            {
                MRUK.Instance.SceneLoadedEvent.AddListener(StartTweakingAsync);
            }
        }

        private void OnDisable()
        {
            if (MRUK.Instance != null)
            {
                MRUK.Instance.SceneLoadedEvent.RemoveListener(StartTweakingAsync);
            }
        }

        /// <summary>
        /// Starts the asynchronous process of tweaking material gradients based on room dimensions.
        /// This method is invoked when the scene is loaded.
        /// </summary>
        public async void StartTweakingAsync()
        {
            try
            {
                await TweakDistanceBasedGradient();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError($"Failed to tweak distance based gradient. Ensure MRUK is initialized and a room is available. Error: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Unexpected error while tweaking distance based gradient: {e.Message}");
            }
        }

        /// <summary>
        /// Tweaks the distance-based gradient on all mesh renderers by interpolating material properties
        /// based on the current room's dimensions over time.
        /// </summary>
        private async Task TweakDistanceBasedGradient()
        {
            var currentRoom = MRUK.Instance.GetCurrentRoom();
            if (currentRoom == null)
            {
                Debug.LogError("Cannot tweak distance based gradient: No current room available. Ensure a room has been loaded.");
                return;
            }

            var roomBounds = currentRoom.GetRoomBounds();
            var roomSize = Mathf.Max(roomBounds.size.x, roomBounds.size.z);
            var roomSizeVec = new Vector2(0, roomSize);
            var allMeshes = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float t = 0;
            while (t < 1)
            {
                foreach (var meshRenderer in allMeshes)
                {
                    if (meshRenderer.material.HasProperty(DistanceCovered))
                    {
                        meshRenderer.material.SetVector(DistanceCovered,
                            Vector2.Lerp(meshRenderer.material.GetVector(DistanceCovered), roomSizeVec, t));
                    }
                }

                t += Time.deltaTime;
                await Task.Yield();
            }
        }
    }
}
