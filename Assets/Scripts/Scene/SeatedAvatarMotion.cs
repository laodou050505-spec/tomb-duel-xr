using Guandan.Game;
using UnityEngine;

namespace Guandan.Scene
{
    /// <summary>Plays a calm sitting loop without ever moving the authored avatar root.</summary>
    public sealed class SeatedAvatarMotion : MonoBehaviour
    {
        private Animator animator;
        private float phase;
        private float reaction;
        private float reactionDirection = 1f;
        private bool preserveRootTransform = true;
        private Vector3 authoredPosition;
        private Quaternion authoredRotation;
        private Vector3 authoredScale;

        public bool PreserveRootTransform
        {
            get => preserveRootTransform;
            set => preserveRootTransform = value;
        }

        public void Configure(float motionPhase)
        {
            phase = motionPhase;
            animator = FindAnimator();
            if (animator != null)
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.speed = Mathf.Lerp(0.88f, 1.05f, Mathf.Repeat(motionPhase, 1f));
                if (animator.runtimeAnimatorController != null)
                {
                    animator.Play(0, 0, Mathf.Repeat(motionPhase, 0.82f));
                    animator.Update(0f);
                }
            }
            authoredPosition = transform.localPosition;
            authoredRotation = transform.localRotation;
            authoredScale = transform.localScale;
        }

        public void Rebase()
        {
            authoredPosition = transform.localPosition;
            authoredRotation = transform.localRotation;
            authoredScale = transform.localScale;
        }

        public void React(SocialPropType type)
        {
            reaction = 1f;
            reactionDirection = type == SocialPropType.Tomato ? -1f : 1f;
        }

        private void Update()
        {
            reaction = Mathf.MoveTowards(reaction, 0f, Time.deltaTime * 1.45f);
        }

        private void LateUpdate()
        {
            // The imported sitting clip owns the skeleton. Root motion and root-level
            // procedural sway are intentionally disabled so an authored Scene placement
            // remains pixel-for-pixel stable while the bones provide the natural loop.
            if (preserveRootTransform)
            {
                transform.localPosition = authoredPosition;
                transform.localRotation = authoredRotation;
                transform.localScale = authoredScale;
            }
        }

        private Animator FindAnimator()
        {
            var animators = GetComponentsInChildren<Animator>(true);
            Animator fallback = null;
            foreach (var candidate in animators)
            {
                if (candidate == null) continue;
                fallback ??= candidate;
                if (candidate.runtimeAnimatorController != null) return candidate;
            }
            return fallback;
        }
    }
}
