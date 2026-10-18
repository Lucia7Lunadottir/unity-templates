using UnityEngine;

namespace PG.MenuManagement
{
    public class TransformUIToObject : MonoBehaviour
    {
        private Camera _camera;
        [SerializeField] private Transform _parent;
        [SerializeField] private Vector3 _offset;
        [SerializeField] private float _speed = 4f;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _camera = Camera.main;
        }

        // Update is called once per frame
        void Update()
        {
            transform.position = Vector3.Lerp(transform.position, _camera.WorldToScreenPoint(_parent.position + _offset), _speed * Time.unscaledDeltaTime);
        }
    }
}
