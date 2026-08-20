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
            if (Mouse.current == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            var ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (Mouse.current.leftButton.wasPressedThisFrame && Physics.Raycast(ray, out var pressedHit, 80f))
            {
                var interactable = pressedHit.collider.GetComponentInParent<IWorldInteractable>();
                interactable?.Interact(PointerSource.Desktop);
                draggingSocialProp = interactable is SocialProp && SocialProp.Held != null;
            }

            if (draggingSocialProp && Mouse.current.leftButton.isPressed && SocialProp.Held != null)
            {
                var distance = Mathf.Clamp(Vector3.Distance(targetCamera.transform.position, SocialProp.Held.transform.position), 1.2f, 8f);
                SocialProp.Held.DragTo(ray.GetPoint(distance));
            }

            if (draggingSocialProp && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (SocialProp.Held != null && SocialProp.Held.ReadyToThrow)
                    FindAvatarAlongRay(ray)?.Interact(PointerSource.Desktop);
                SocialProp.Held?.ReturnHome();
                draggingSocialProp = false;
            }
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
    }
}
