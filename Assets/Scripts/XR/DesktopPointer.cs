using Guandan.Game;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Guandan.XR
{
    public sealed class DesktopPointer : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        private bool draggingSocialProp;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private void Update()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            // PICO Emulator forwards its hand-ray pinch to the Unity activity as an Android
            // touch. Some runtime versions expose the native aim ray but omit its pinch state,
            // so the touch must remain active even while that ray is available. UiHitTarget
            // deduplicates the two routes if a headset reports both for one physical pinch.
            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
            {
                var touchPosition = touch.primaryTouch.position.ReadValue();
                var touchRay = targetCamera.ScreenPointToRay(touchPosition);
                if (touch.primaryTouch.press.wasPressedThisFrame)
                    BeginTouchInteraction(touchPosition, touchRay);

                if (draggingSocialProp && touch.primaryTouch.press.isPressed && SocialProp.Held != null)
                    DragHeldProp(touchRay);

                if (draggingSocialProp && touch.primaryTouch.press.wasReleasedThisFrame)
                    EndInteraction(touchRay);
                return;
            }

            // The editor and desktop player expose a regular Mouse device.  It uses the same
            // direct UGUI hit test before falling back to world objects.
            if (Mouse.current != null)
            {
                var mousePosition = Mouse.current.position.ReadValue();
                var mouseRay = targetCamera.ScreenPointToRay(mousePosition);
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    BeginScreenOrWorldInteraction(mousePosition, mouseRay);

                if (draggingSocialProp && Mouse.current.leftButton.isPressed && SocialProp.Held != null)
                    DragHeldProp(mouseRay);

                if (draggingSocialProp && Mouse.current.leftButton.wasReleasedThisFrame)
                    EndInteraction(mouseRay);
            }

        }

        private void BeginTouchInteraction(Vector2 screenPosition, Ray ray)
        {
            // This explicit UI route is the reliable hand-pinch path in PICO Emulator.  The
            // physics route remains available for props and authored tomb interactions.
            BeginScreenOrWorldInteraction(screenPosition, ray);
        }

        private void BeginScreenOrWorldInteraction(Vector2 screenPosition, Ray ray)
        {
            var screenUi = FindFirstObjectByType<Guandan.UI.ScreenGameUi>();
            if (screenUi != null && screenUi.TryHandleScreenPress(screenPosition, PointerSource.Desktop))
            {
                draggingSocialProp = false;
                return;
            }
            BeginInteraction(ray);
        }

        private void BeginInteraction(Ray ray)
        {
            if (!TryInteractAtRay(ray, out var interactable)) return;
            interactable.Interact(PointerSource.Desktop);
            draggingSocialProp = interactable is SocialProp && SocialProp.Held != null;
        }

        private void DragHeldProp(Ray ray)
        {
            var distance = Mathf.Clamp(Vector3.Distance(targetCamera.transform.position, SocialProp.Held.transform.position), 1.2f, 8f);
            SocialProp.Held.DragTo(ray.GetPoint(distance));
        }

        private void EndInteraction(Ray ray)
        {
            if (SocialProp.Held != null && SocialProp.Held.ReadyToThrow)
                FindAvatarAlongRay(ray)?.Interact(PointerSource.Desktop);
            SocialProp.Held?.ReturnHome();
            draggingSocialProp = false;
        }

        private static Guandan.Game.AvatarTarget FindAvatarAlongRay(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 80f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var avatar = hit.collider.GetComponentInParent<Guandan.Game.AvatarTarget>();
                if (avatar != null) return avatar;
            }
            return null;
        }

        /// <summary>
        /// The playable cards and commands are a head-relative, flat canvas.  It must win
        /// against the tomb/table colliders behind it, otherwise a visually obvious card
        /// can appear to ignore a mouse click simply because the table was returned first.
        /// World props remain reachable whenever the ray is not over a UI control.
        /// </summary>
        private static bool TryInteractAtRay(Ray ray, out IWorldInteractable result)
        {
            var hits = Physics.RaycastAll(ray, 80f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (var hit in hits)
            {
                var uiTarget = hit.collider.GetComponentInParent<Guandan.UI.UiHitTarget>();
                if (uiTarget != null && uiTarget.isActiveAndEnabled)
                {
                    result = uiTarget;
                    return true;
                }
            }

            foreach (var hit in hits)
            {
                var interactable = hit.collider.GetComponentInParent<IWorldInteractable>();
                if (interactable != null)
                {
                    result = interactable;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
