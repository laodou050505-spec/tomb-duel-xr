using UnityEngine;

namespace Guandan.Game
{
    public sealed class WorldButton : MonoBehaviour, IWorldInteractable
    {
        [SerializeField] private GameAction action;
        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer buttonRenderer;
        private GameDirector director;
        private bool available = true;

        public bool IsAvailable => available;

        public GameAction Action => action;

        public void Configure(GameDirector owner, GameAction configuredAction, string text)
        {
            director = owner;
            action = configuredAction;
            EnsureVisuals();
            label = GetComponentInChildren<TextMesh>(true);
            buttonRenderer = GetComponentInChildren<Renderer>(true);
            ApplyTombButtonSkin();
            if (label != null)
            {
                label.text = text;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.024f;
            }
        }

        public void SetAvailable(bool value)
        {
            available = value;
            gameObject.SetActive(value);
        }

        public void Interact(PointerSource source)
        {
            if (!available) return;
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            director?.HandleAction(action);
        }

        private void EnsureVisuals()
        {
            if (GetComponent<BoxCollider>() == null)
            {
                var collider = gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.82f, 0.08f, 0.38f);
            }

            if (GetComponentInChildren<Renderer>(true) == null)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "ButtonVisual";
                visual.transform.SetParent(transform, false);
                visual.transform.localScale = new Vector3(0.82f, 0.08f, 0.38f);
                var visualCollider = visual.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(visualCollider); else DestroyImmediate(visualCollider);
            }

            if (GetComponentInChildren<TextMesh>(true) == null)
            {
                var textObject = new GameObject("Label");
                textObject.transform.SetParent(transform, false);
                textObject.transform.localPosition = new Vector3(0f, 0.055f, 0f);
                textObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var text = textObject.AddComponent<TextMesh>();
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.024f;
            }
        }

        private void ApplyTombButtonSkin()
        {
            if (buttonRenderer == null) return;
            var texture = Resources.Load<Texture2D>("GuandanUI/TombButton");
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            var material = new Material(shader);
            material.mainTexture = texture;
            material.color = texture != null ? Color.white : new Color(0.31f, 0.16f, 0.06f);
            buttonRenderer.material = material;
        }
    }
}
