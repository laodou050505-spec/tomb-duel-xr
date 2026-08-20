using System;
using System.Linq;
using UnityEngine;

namespace Guandan.Scene
{
    /// <summary>
    /// Runtime presentation for a character the designer placed directly in the Scene.
    /// The component deliberately owns only the Animator and renderer presentation; the
    /// authored root Transform is captured and restored every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AuthoredAvatarPresentation : MonoBehaviour
    {
        private Vector3 authoredPosition;
        private Quaternion authoredRotation;
        private Vector3 authoredScale;
        private bool baselineCaptured;
        private Animator activeAnimator;
        private Transform runtimeSitProxy;

        public int AvatarIndex { get; private set; }
        public bool PreserveRootTransform { get; private set; } = true;
        public Animator ActiveAnimator => activeAnimator;

        public void Configure(int avatarIndex, float phase)
        {
            AvatarIndex = avatarIndex;
            PreserveRootTransform = true;
            CaptureBaseline();

            // Scene-authored FBX roots are importer-owned. Attaching an Animator to
            // their hierarchy can fail silently on certain model instances, leaving a
            // static character even though the gameplay bindings continue.  Always
            // use the matching runtime prefab as a child proxy instead: it contains a
            // regular Animator plus a valid Avatar, while the authored root remains
            // untouched in position, rotation and scale.
            activeAnimator = CreateRuntimeSitProxy(avatarIndex);
            if (activeAnimator == null)
            {
                Debug.LogWarning($"[GuandanAvatar] {name} 无法创建 Animator，保留静态模型但不阻断其他交互。");
                return;
            }
            activeAnimator.applyRootMotion = false;
            activeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            activeAnimator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(
                $"GuandanAvatars/Avatar_{avatarIndex}_Sit");
            activeAnimator.enabled = activeAnimator.runtimeAnimatorController != null;
            if (activeAnimator.enabled)
            {
                activeAnimator.speed = Mathf.Lerp(0.94f, 1.02f, Mathf.Repeat(phase, 1f));
                activeAnimator.Play(0, 0, Mathf.Repeat(phase, 0.73f));
                activeAnimator.Update(0f);
            }
        }

        private Animator CreateRuntimeSitProxy(int avatarIndex)
        {
            var prefab = Resources.Load<GameObject>($"GuandanAvatars/Avatar_{avatarIndex}");
            if (prefab == null)
            {
                Debug.LogWarning($"[GuandanAvatar] 缺少运行时坐姿预制体 Avatar_{avatarIndex}。");
                return null;
            }
            var proxy = Instantiate(prefab, transform);
            proxy.name = $"{name} · 坐姿动画代理";
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one;
            runtimeSitProxy = proxy.transform;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (runtimeSitProxy != null && renderer.transform.IsChildOf(runtimeSitProxy)) continue;
                renderer.enabled = false;
            }
            RuntimeGameplayBindings.ApplyMatteAvatarMaterials(runtimeSitProxy);
            RuntimeGameplayBindings.SmoothAvatarMeshes(runtimeSitProxy);

            // The imported FBX keeps the skeleton under tripo_convert/Armature, while
            // the sitting clip addresses paths beginning with Armature.  An Animator
            // on Avatar_n therefore cannot resolve those curves. Bind the controller
            // to the model root (the direct parent of Armature) and disable any stale
            // outer Animator left by an older prefab-generation pass.
            var animatorHost = FindAnimatorHost(proxy.transform);
            if (animatorHost == null)
            {
                Debug.LogWarning($"[GuandanAvatar] {name} 坐姿代理没有找到 Animator 宿主节点。");
                return null;
            }

            // UnityEngine.Object can carry a fake-null component after an imported
            // prefab is cloned. Use an explicit Unity null check before falling back
            // to AddComponent; C#'s ?? would otherwise keep the invalid reference.
            var animator = animatorHost.GetComponent<Animator>();
            if (animator == null) animator = animatorHost.gameObject.AddComponent<Animator>();
            foreach (var candidate in proxy.GetComponentsInChildren<Animator>(true))
            {
                if (candidate == null || candidate == animator) continue;
                candidate.enabled = false;
                candidate.runtimeAnimatorController = null;
            }
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            return animator;
        }

        private static Transform FindAnimatorHost(Transform root)
        {
            if (root == null) return null;
            var armature = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item != null
                    && item.name.Equals("Armature", StringComparison.OrdinalIgnoreCase));
            return armature != null && armature.parent != null ? armature.parent : root;
        }

        public void CaptureBaseline()
        {
            authoredPosition = transform.position;
            authoredRotation = transform.rotation;
            authoredScale = transform.localScale;
            baselineCaptured = true;
        }

        private void LateUpdate()
        {
            if (!baselineCaptured || !PreserveRootTransform) return;
            transform.SetPositionAndRotation(authoredPosition, authoredRotation);
            transform.localScale = authoredScale;
            if (runtimeSitProxy != null)
            {
                runtimeSitProxy.localPosition = Vector3.zero;
                runtimeSitProxy.localRotation = Quaternion.identity;
                runtimeSitProxy.localScale = Vector3.one;
            }
        }
    }
}
