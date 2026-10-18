using System.Collections;
using UnityEngine;

namespace PG.MenuManagement
{
    public class UIHideByTime : MonoBehaviour
    {
        [SerializeField] private UIShowHide _uiShowHide;
        [SerializeField] private float _duration = 3f;

        private Coroutine _coroutine;

        private void OnEnable()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
            }

            _coroutine = StartCoroutine(DisableUI());
        }

        private void OnDisable()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
            }
        }

        IEnumerator DisableUI()
        {
            yield return new WaitForSecondsRealtime(_duration);
            _uiShowHide.Hide();
        }
    }
}
