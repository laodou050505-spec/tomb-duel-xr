using System.Collections;
using UnityEngine;

namespace Guandan.Game
{
    public sealed class SocialProp : MonoBehaviour, IWorldInteractable
    {
        public static SocialProp Held { get; private set; }

        [SerializeField] private SocialPropType propType;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Renderer[] renderers;
        private Coroutine liftRoutine;
        private Vector3 pendingDragPosition;
        private Vector3 dragTarget;
        private Vector3 dragVelocity;
        private bool hasPendingDrag;
        private bool liftComplete;

        public SocialPropType PropType => propType;
        public bool ReadyToThrow => liftComplete;

        public void Configure(SocialPropType type)
        {
            propType = type;
        }

        private void Awake()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            renderers = GetComponentsInChildren<Renderer>(true);
            dragTarget = homePosition;
        }

        private void Update()
        {
            if (Held != this || !liftComplete) return;
            // A held prop should feel like a hand carrying it, not a teleporting cursor.
            transform.position = Vector3.SmoothDamp(transform.position, dragTarget, ref dragVelocity, 0.16f, 4.8f);
        }

        public void Interact(PointerSource source)
        {
            if (Held == this)
            {
                ReturnHome();
                return;
            }

            if (Held != null) Held.ReturnHome();
            Held = this;
            liftComplete = false;
            hasPendingDrag = false;
            dragVelocity = Vector3.zero;
            liftRoutine = StartCoroutine(LiftOutRoutine());
            FindAnyObjectByType<GameDirector>()?.NotifySocialPropPickup(propType);
            foreach (var item in renderers)
            {
                if (item.material.HasProperty("_EmissionColor"))
                    item.material.SetColor("_EmissionColor", propType == SocialPropType.Flower ? Color.magenta * 0.3f : Color.red * 0.3f);
            }
        }

        public void ReturnHome()
        {
            if (liftRoutine != null) StopCoroutine(liftRoutine);
            liftRoutine = null;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            dragTarget = homePosition;
            dragVelocity = Vector3.zero;
            liftComplete = false;
            hasPendingDrag = false;
            if (renderers != null)
            {
                foreach (var item in renderers)
                {
                    item.enabled = true;
                    if (item.material.HasProperty("_EmissionColor")) item.material.SetColor("_EmissionColor", Color.black);
                }
            }
            if (Held == this) Held = null;
        }

        public void DragTo(Vector3 worldPosition)
        {
            if (Held != this) return;
            if (!liftComplete)
            {
                pendingDragPosition = worldPosition;
                hasPendingDrag = true;
                return;
            }
            dragTarget = worldPosition;
        }

        /// <summary>Animate the prop to the target's body-facing side, then return it to the basket.</summary>
        public void ThrowTo(System.Func<Vector3> targetPosition, System.Action<Vector3> onImpact = null)
        {
            if (!liftComplete) return;
            if (liftRoutine != null) StopCoroutine(liftRoutine);
            liftRoutine = StartCoroutine(ThrowRoutine(targetPosition, onImpact));
        }

        private IEnumerator ThrowRoutine(System.Func<Vector3> targetPosition, System.Action<Vector3> onImpact)
        {
            var start = transform.position;
            var duration = Mathf.Clamp(Vector3.Distance(start, targetPosition()) / 7f, 0.32f, 0.65f);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / duration);
                var eased = progress;
                var position = Vector3.Lerp(start, targetPosition(), eased);
                position.y += Mathf.Sin(progress * Mathf.PI) * 0.34f;
                transform.position = position;
                transform.rotation = Quaternion.Slerp(homeRotation, homeRotation * Quaternion.Euler(0f, 0f, propType == SocialPropType.Flower ? 24f : -24f), eased);
                yield return null;
            }
            transform.position = targetPosition();
            onImpact?.Invoke(transform.position);
            // Hide the intact tomato at impact; the body splat now owns the visual.
            foreach (var item in renderers) if (item != null) item.enabled = false;
            yield return new WaitForSeconds(0.12f);
            foreach (var item in renderers) if (item != null) item.enabled = true;
            ReturnHome();
        }

        private IEnumerator LiftOutRoutine()
        {
            var start = homePosition;
            var camera = Camera.main;
            var outward = camera != null
                ? Vector3.ProjectOnPlane(camera.transform.position - start, Vector3.up)
                : Vector3.back;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.back;
            outward.Normalize();

            var boundsHeight = 0.6f;
            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                boundsHeight = bounds.size.y;
            }
            var liftHeight = Mathf.Max(0.82f, boundsHeight * 0.78f);
            var end = start + Vector3.up * liftHeight + outward * 0.24f;
            const float duration = 0.36f;
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                if (Held != this) yield break;
                var progress = Mathf.Clamp01(elapsed / duration);
                var vertical = 1f - Mathf.Pow(1f - progress, 3f);
                var exit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, progress));
                transform.position = start + Vector3.up * (liftHeight * vertical) + outward * (0.24f * exit);
                transform.rotation = Quaternion.Slerp(homeRotation, Quaternion.Euler(0f, 8f, -5f) * homeRotation, exit);
                yield return null;
            }

            transform.SetPositionAndRotation(end, homeRotation);
            dragTarget = end;
            liftComplete = true;
            liftRoutine = null;
            if (hasPendingDrag)
            {
                pendingDragPosition.y = Mathf.Max(pendingDragPosition.y, start.y + liftHeight * 0.82f);
                transform.position = pendingDragPosition;
            }
        }

        public static SocialProp ConsumeHeld()
        {
            var held = Held;
            if (held == null || !held.ReadyToThrow) return null;
            Held = null;
            return held;
        }
    }
}
