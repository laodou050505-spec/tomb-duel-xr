using UnityEngine;
using System.Linq;

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
            var director = FindFirstObjectByType<GameDirector>();
            var avatar = director != null ? director.GetAvatarTransform(seat) : transform;
            var impact = GetImpactPoint(avatar != null ? avatar : transform);
            prop.ThrowTo(() => avatar != null ? GetImpactPoint(avatar) : impact, hitPoint =>
            {
                if (director != null) director.UseSocialProp(prop.PropType, seat, hitPoint);
            });
        }

        public static Transform GetImpactAttachment(Transform avatar)
        {
            var animator = avatar.GetComponentsInChildren<Animator>().FirstOrDefault(item => item.enabled && item.isHuman);
            if (animator == null) return avatar;
            return animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine) ?? avatar;
        }

        public static Vector3 GetImpactPoint(Transform avatar)
        {
            // Use the actual body, excluding the sauce that is attached beneath it.
            var renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Where(item => item.enabled && item.gameObject.activeInHierarchy).Cast<Renderer>().ToArray();
            if (renderers.Length == 0)
                renderers = avatar.GetComponentsInChildren<Renderer>().Where(item => item.enabled && item.gameObject.activeInHierarchy && item.name != "Sauce patch" && item.name != "Tomato seed" && item.name != "Juice drop").ToArray();
            var bounds = new Bounds(avatar.position + Vector3.up, Vector3.one);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            var viewer = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var front = Vector3.ProjectOnPlane(viewer - bounds.center, Vector3.up).normalized;
            if (front.sqrMagnitude < 0.01f) front = -avatar.forward;
            var point = new Vector3(bounds.center.x, Mathf.Lerp(bounds.min.y, bounds.max.y, 0.68f), bounds.center.z);
            var chest = GetImpactAttachment(avatar);
            if (chest != avatar) return chest.position + Vector3.up * 0.04f + front * 0.30f;
            return point + front * Mathf.Min(0.34f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.70f);
        }
    }
}
