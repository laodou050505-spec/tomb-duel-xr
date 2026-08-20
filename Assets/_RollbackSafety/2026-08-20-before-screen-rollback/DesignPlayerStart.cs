using UnityEngine;

namespace Guandan.XR
{
    /// <summary>
    /// The single authored gameplay entry pose.  It is intentionally a scene component,
    /// rather than a camera-local offset, so desktop, PICO startup, and recenter all
    /// point at the same real location in the tomb.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesignPlayerStart : MonoBehaviour
    {
        [Header("南家桌边 · 首个可见玩法地标为牌桌中央")]
        [SerializeField] private Vector3 eyePosition = new(0f, 3.12f, -4.65f);
        [SerializeField] private Vector3 gameplayFocus = new(0f, 2.14f, 0f);
        [SerializeField, Min(0.1f)] private float eyeHeight = 1.60f;

        [Header("夺宝台 · 仅由实体入口台触发前往")]
        [SerializeField] private Vector3 treasureEyePosition = new(0f, 3.00f, 18.25f);
        [SerializeField] private Vector3 treasureGameplayFocus = new(0f, 0.90f, 25.60f);

        public Vector3 EyePosition => eyePosition;
        public Vector3 GameplayFocus => gameplayFocus;
        public float EyeHeight => eyeHeight;
        public Vector3 TreasureEyePosition => treasureEyePosition;
        public Vector3 TreasureGameplayFocus => treasureGameplayFocus;

        public void ConfigureFallback(
            Vector3 authoredEyePosition,
            Vector3 authoredGameplayFocus,
            float authoredEyeHeight,
            Vector3 authoredTreasureEyePosition,
            Vector3 authoredTreasureGameplayFocus)
        {
            eyePosition = authoredEyePosition;
            gameplayFocus = authoredGameplayFocus;
            eyeHeight = Mathf.Max(0.1f, authoredEyeHeight);
            treasureEyePosition = authoredTreasureEyePosition;
            treasureGameplayFocus = authoredTreasureGameplayFocus;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 0.92f, 0.76f, 0.90f);
            Gizmos.DrawWireSphere(eyePosition, 0.16f);
            Gizmos.DrawLine(eyePosition, gameplayFocus);
            Gizmos.DrawWireCube(gameplayFocus, new Vector3(0.26f, 0.05f, 0.26f));
        }
    }
}
