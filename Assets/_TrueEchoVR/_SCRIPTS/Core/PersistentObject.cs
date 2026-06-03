using UnityEngine;

namespace TEVR.Core
{
    public class PersistentObject : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
    }
}
