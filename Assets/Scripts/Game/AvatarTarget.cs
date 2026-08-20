using UnityEngine;

namespace Guandan.Game
{
    public sealed class AvatarTarget : MonoBehaviour, IWorldInteractable
    {
        [SerializeField] private PlayerSeat seat;

        public void Configure(PlayerSeat targetSeat)
        {
            seat = targetSeat;
        }

        public void Interact(PointerSource source)
        {
            var prop = SocialProp.ConsumeHeld();
            if (prop == null) return;
            FindFirstObjectByType<GameDirector>()?.UseSocialProp(prop.PropType, seat);
            var renderers = GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position + Vector3.up, Vector3.one);
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            // Imported characters face local -Z. Their body-facing side is therefore
            // opposite transform.forward and always remains between the character and
            // the table, never behind the avatar.
            var front = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
            if (front.sqrMagnitude < 0.01f) front = Vector3.forward;
            // Put the effect on the table-facing side of the body. The small forward
            // offset keeps the prop visible in front of the avatar instead of behind it.
            prop.ThrowTo(bounds.center + front * 0.48f + Vector3.up * 0.02f);
        }
    }
}
