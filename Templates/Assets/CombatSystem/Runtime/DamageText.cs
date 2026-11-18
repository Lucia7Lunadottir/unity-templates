using System.Collections;
using PG.Tween;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class DamageText : MonoBehaviour
{
    [SerializeField] private TMP_Text _damageTextReference;
    private WaitForSecondsRealtime _waitTime;
    private WaitForSecondsRealtime _waitTransitionTime;
    [SerializeField] private float _transitionDuration = 0.2f;
    [SerializeField] private float _duration = 0.5f;
    [SerializeField] private Vector3 _baseScale = Vector3.one;
    
    private TransformHandle _transformHandle;
    
    private void Awake()
    {
        _transformHandle = transformHandle;
        _waitTime =  new WaitForSecondsRealtime(1f);
        _waitTransitionTime =  new WaitForSecondsRealtime(_transitionDuration);
    }

    public void StartVisual(float damage, AssetReferenceGameObject assetReferenceGameObject)
    {
        _damageTextReference.text = $"-{damage}";
        StartCoroutine(RemoveDamageText(gameObject, assetReferenceGameObject));
    }

    private IEnumerator RemoveDamageText(GameObject instance, AssetReferenceGameObject assetReferenceGameObject)
    {
        PGTween.OnValueTween(Vector3.zero, _baseScale, 0.2f, true, v => _transformHandle.localScale = v);
        yield return _waitTime;
        instance.transform.OnTransformScaleTween(Vector3.zero, _transitionDuration, true);
        yield return _waitTransitionTime;
        assetReferenceGameObject.ReleaseInstance(instance);
    }
}