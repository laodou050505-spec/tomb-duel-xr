using UnityEngine;

namespace Guandan.Game
{
    [DisallowMultipleComponent]
    public sealed class CardView : MonoBehaviour, IWorldInteractable
    {
        private Card card;
        private GameDirector director;
        private bool selectable;
        private bool selected;
        private bool highlighted;
        private Renderer faceRenderer;
        private TextMesh label;
        private Vector3 baseLocalPosition;

        public Card Card => card;
        public bool Selected => selected;

        public void Configure(Card value, GameDirector owner, bool canSelect, bool faceUp = true)
        {
            card = value;
            director = owner;
            selectable = canSelect;
            faceRenderer = GetComponentInChildren<Renderer>(true);
            label = GetComponentInChildren<TextMesh>(true);
            if (label != null)
            {
                label.text = faceUp ? value.ShortLabel : "掼\n蛋";
                label.color = faceUp && value.IsRed ? new Color(0.68f, 0.08f, 0.06f) : new Color(0.08f, 0.07f, 0.05f);
            }
            ApplyVisual();
        }

        public void CaptureBasePosition()
        {
            baseLocalPosition = transform.localPosition;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            transform.localPosition = baseLocalPosition + (selected ? new Vector3(0f, 0.14f, 0.10f) : Vector3.zero);
            ApplyVisual();
        }

        public void SetHighlighted(bool value)
        {
            highlighted = value;
            ApplyVisual();
        }

        public void Interact(PointerSource source)
        {
            if (!selectable || card == null) return;
            director?.ToggleCard(this);
        }

        private void ApplyVisual()
        {
            if (faceRenderer == null) return;
            var color = selected
                ? new Color(0.94f, 0.62f, 0.18f)
                : highlighted
                    ? new Color(0.82f, 0.25f, 0.14f)
                    : new Color(0.90f, 0.84f, 0.68f);
            faceRenderer.material.color = color;
        }
    }
}
