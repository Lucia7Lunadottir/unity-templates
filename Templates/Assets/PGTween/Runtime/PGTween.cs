using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.Tween
{
    public static class PGTween
    {
        private static CancellationTokenSource _globalCts;

        // Инициализируем или сбрасываем токен при каждом запуске Play Mode
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = new CancellationTokenSource();
        }

        // Централизованный метод остановки всех твинов
        public static void StopAllTweens(this MonoBehaviour monoBehaviour)
        {
            if (_globalCts != null)
            {
                _globalCts.Cancel();
                _globalCts.Dispose();
            }
            _globalCts = new CancellationTokenSource();
        }

        // Вспомогательный метод для получения безопасного токена,
        // который сам отменится при выходе из игры или принудительном StopAllTweens
        private static CancellationToken GetSafeToken()
        {
            if (_globalCts == null) ResetStaticState();

            // Если мы вообще не в Play Mode — возвращаем уже отмененный токен,
            // чтобы код даже не начал выполняться в редакторе
            if (!Application.isPlaying)
                return new CancellationToken(true);

            return _globalCts.Token;
        }

        public static async Awaitable Delay(float time, bool useIgnoreTimeScale = false, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                float elapsedTime = 0f;
                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    // Жесткая привязка к кадру Unity. Вне Runtime работать не будет!
                    await Awaitable.NextFrameAsync();
                }
                ended?.Invoke();
            }
            catch (OperationCanceledException) { /* Твин остановлен */ }
        }

        public static async Awaitable DelayTask(float time, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                await Awaitable.WaitForSecondsAsync(time, cancellationToken: token);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnMaterialValueTween(this Material material, string parameter, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                float from = material.GetFloat(parameter);

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!material) return;

                    material.SetFloat(parameter, Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time)));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (material) material.SetFloat(parameter, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnMaterialValueTween(this Material material, string parameter, Color to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Color from = material.GetColor(parameter);

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!material) return;

                    material.SetColor(parameter, Color.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time)));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (material) material.SetColor(parameter, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnAlphaTween(this CanvasGroup canvasGroup, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                float from = canvasGroup.alpha;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!canvasGroup) return;

                    canvasGroup.alpha = Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (canvasGroup) canvasGroup.alpha = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnAlphaTween(this Image image, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Color from = image.color;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!image) return;

                    image.color = Color.LerpUnclamped(from, new Color(image.color.r, image.color.g, image.color.b, to), animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (image) image.color = new Color(image.color.r, image.color.g, image.color.b, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnAlphaTween(this TMP_Text tmpText, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                float from = tmpText.alpha;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!tmpText) return;

                    tmpText.alpha = Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (tmpText) tmpText.alpha = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnColorTween(this Image image, Color to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Color from = image.color;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!image) return;

                    image.color = Color.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (image) image.color = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformTween(this Transform transform, Vector3 to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Vector3 from = transform.position;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.position = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.position = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformLocalTween(this Transform transform, Vector3 to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Vector3 from = transform.localPosition;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.localPosition = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.localPosition = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformScaleTween(this Transform transform, Vector3 to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Vector3 from = transform.localScale;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.localScale = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.localScale = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformRotationTween(this Transform transform, Quaternion to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Quaternion from = transform.rotation;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.rotation = Quaternion.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.rotation = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnAnimFloatTween(this Animator animator, string parameter, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                float from = animator.GetFloat(parameter);

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!animator) return;

                    animator.SetFloat(parameter, Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time)));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (animator) animator.SetFloat(parameter, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnValueTween(float from, float to, float time, bool useIgnoreTimeScale = false, Action<float> onUpdate = default, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    float t = elapsedTime / time;
                    float value = Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    onUpdate?.Invoke(value);

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                onUpdate?.Invoke(to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnValueTween(Vector3 from, Vector3 to, float time, bool useIgnoreTimeScale = false, Action<Vector3> onUpdate = default, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    float t = elapsedTime / time;
                    Vector3 value = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    onUpdate?.Invoke(value);

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                onUpdate?.Invoke(to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static void EnableUITween(this CanvasGroup canvasGroup)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        public static void DisableUITween(this CanvasGroup canvasGroup)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        public static async Awaitable OnAlphaTween(this Material material, float to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Color from = material.color;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!material) return;

                    material.color = Color.LerpUnclamped(from, new Color(material.color.r, material.color.g, material.color.b, to), animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (material) material.color = new Color(material.color.r, material.color.g, material.color.b, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnColorTween(this Material material, Color to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Color from = material.color;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!material) return;

                    float t = elapsedTime / time;
                    material.color = Color.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (material) material.color = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformLocalEulerAnglesTween(this Transform transform, Vector3 to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Vector3 from = transform.localEulerAngles;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.localEulerAngles = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.localEulerAngles = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformEulerAnglesTween(this Transform transform, Vector3 to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Vector3 from = transform.eulerAngles;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.eulerAngles = Vector3.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.eulerAngles = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnTransformLocalRotationTween(this Transform transform, Quaternion to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                Quaternion from = transform.localRotation;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!transform) return;

                    transform.localRotation = Quaternion.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (transform) transform.localRotation = to;
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnAnimIntTween(this Animator animator, string parameter, int to, float time, bool useIgnoreTimeScale = false, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;
                int from = animator.GetInteger(parameter);

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();
                    if (!animator) return;

                    // Примечание: В оригинальном коде тут использовался animator.SetFloat, что вызывало ошибку для Integer-параметров.
                    // Я исправила на SetInteger с округлением.
                    float lerpedValue = Mathf.LerpUnclamped(from, to, animationCurve.Evaluate(elapsedTime / time));
                    animator.SetInteger(parameter, Mathf.RoundToInt(lerpedValue));

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                if (animator) animator.SetInteger(parameter, to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnValueTween(Quaternion from, Quaternion to, float time, bool useIgnoreTimeScale = false, Action<Quaternion> onUpdate = default, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    float t = elapsedTime / time;
                    Quaternion value = Quaternion.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    onUpdate?.Invoke(value);

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                onUpdate?.Invoke(to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnValueTween(Vector2 from, Vector2 to, float time, bool useIgnoreTimeScale = false, Action<Vector2> onUpdate = default, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    float t = elapsedTime / time;
                    Vector2 value = Vector2.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    onUpdate?.Invoke(value);

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                onUpdate?.Invoke(to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        public static async Awaitable OnValueTween(Color from, Color to, float time, bool useIgnoreTimeScale = false, Action<Color> onUpdate = default, AnimationCurve animationCurve = null, Action ended = null)
        {
            var token = GetSafeToken();
            try
            {
                animationCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
                float elapsedTime = 0f;

                while (elapsedTime < time)
                {
                    token.ThrowIfCancellationRequested();

                    float t = elapsedTime / time;
                    Color value = Color.LerpUnclamped(from, to, animationCurve.Evaluate(t));
                    onUpdate?.Invoke(value);

                    elapsedTime += useIgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    await Awaitable.NextFrameAsync();
                }

                onUpdate?.Invoke(to);
                ended?.Invoke();
            }
            catch (OperationCanceledException) { }
        }
    }
}
