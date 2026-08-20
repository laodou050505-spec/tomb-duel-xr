using UnityEngine;

namespace Guandan.Game
{
    /// <summary>
    /// A real, ray-selectable bamboo lot placed above the table. It replaces the old
    /// camera canvas lottery cards so choosing a companion is part of the room itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLotteryLot : MonoBehaviour, IWorldInteractable
    {
        [SerializeField] private int index;
        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer bodyRenderer;
        private GameDirector director;
        private bool available;

        public void Configure(GameDirector owner, int lotIndex)
        {
            director = owner;
            index = lotIndex;
            EnsureVisuals();
        }

        public void SetPresentation(string text, bool visible, bool selectable)
        {
            EnsureVisuals();
            available = selectable;
            gameObject.SetActive(visible);
            if (label != null) label.text = text;
            if (bodyRenderer != null)
            {
                var color = selectable
                    ? new Color(0.60f, 0.38f, 0.14f)
                    : new Color(0.26f, 0.19f, 0.11f);
                bodyRenderer.material.color = color;
            }
        }

        public void Interact(PointerSource source)
        {
            if (!available) return;
            if (director == null) director = FindFirstObjectByType<GameDirector>();
            director?.ChooseLottery(index);
        }

        private void EnsureVisuals()
        {
            if (GetComponent<BoxCollider>() == null)
            {
                var collider = gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.48f, 0.68f, 0.12f);
                collider.center = new Vector3(0f, 0.34f, 0f);
            }

            if (bodyRenderer == null)
            {
                var body = transform.Find("BambooSlip");
                if (body == null)
                {
                    var bodyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bodyObject.name = "BambooSlip";
                    bodyObject.transform.SetParent(transform, false);
                    bodyObject.transform.localPosition = new Vector3(0f, 0.34f, 0f);
                    bodyObject.transform.localScale = new Vector3(0.44f, 0.64f, 0.08f);
                    var visualCollider = bodyObject.GetComponent<Collider>();
                    if (Application.isPlaying) Destroy(visualCollider); else DestroyImmediate(visualCollider);
                    body = bodyObject.transform;
                }
                bodyRenderer = body.GetComponent<Renderer>();
            }

            if (bodyRenderer != null)
            {
                var texture = Resources.Load<Texture2D>("GuandanUI/TombPanel");
                var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
                var material = new Material(shader) { mainTexture = texture, color = texture != null ? Color.white : new Color(0.16f, 0.10f, 0.05f) };
                bodyRenderer.material = material;
            }

            if (label == null)
            {
                var labelTransform = transform.Find("Label");
                if (labelTransform == null)
                {
                    var labelObject = new GameObject("Label");
                    labelObject.transform.SetParent(transform, false);
                    labelObject.transform.localPosition = new Vector3(0f, 0.34f, -0.051f);
                    labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    label = labelObject.AddComponent<TextMesh>();
                    label.anchor = TextAnchor.MiddleCenter;
                    label.alignment = TextAlignment.Center;
                    // The bamboo slip is 0.42m wide: keep the engraving within it.
                    label.characterSize = 0.027f;
                    label.fontSize = 52;
                    label.color = new Color(0.98f, 0.84f, 0.46f);
                }
                else label = labelTransform.GetComponent<TextMesh>();
            }
            if (label != null)
            {
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = 0.027f;
            }
        }
    }
}
