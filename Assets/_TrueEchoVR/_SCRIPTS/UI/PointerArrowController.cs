using UnityEngine;

namespace TEVR
{
    public class PointerArrowController : MonoBehaviour
    {
        private Transform _target;

        public void SetTarget(Transform target)
        {
            _target = target;
            gameObject.SetActive(_target != null);
        }

        private void Update()
        {
            if (_target == null)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }

            Vector3 toTarget = _target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
            }
        }
    }
}
