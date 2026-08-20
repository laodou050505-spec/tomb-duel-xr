using UnityEngine;

namespace Guandan.Game
{
    /// <summary>Clickable reminder bell. It only shortens the active AI's current thought.</summary>
    public sealed class BellReminder : MonoBehaviour, IWorldInteractable
    {
        private Renderer[] renderers;
        private Color[] baseColors;
        private float pulseUntil;

        public void Interact(PointerSource source)
        {
            var director = FindFirstObjectByType<GameDirector>();
            director?.RemindCurrentAi();
            pulseUntil = Time.time + 0.45f;
            if (director != null) director.PlayBellFeedback(transform.position);
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            baseColors = new Color[renderers.Length];
            for (var i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].material.color;
        }

        private void Update()
        {
            if (renderers == null || renderers.Length == 0) return;
            var active = Time.time < pulseUntil;
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].material.color = active ? Color.Lerp(baseColors[i], new Color(1f, 0.72f, 0.25f), 0.65f) : baseColors[i];
            }
        }
    }
}
