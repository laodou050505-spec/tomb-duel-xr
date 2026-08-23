using Guandan.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Guandan.UI
{
    public enum UiHitKind
    {
        Card,
        Action,
        Lottery,
        Profile,
        CloseProfile,
    }

    /// <summary>Bridges world-space UI to the same pointer ray used by cards and props.</summary>
    public sealed class UiHitTarget : MonoBehaviour, IWorldInteractable
    {
        [SerializeField] private UiHitKind kind;
        [SerializeField] private int index;
        [SerializeField] private string value;
        private ScreenGameUi owner;
        private float lastDispatchTime = -10f;

        public void Configure(ScreenGameUi screen, UiHitKind hitKind, int hitIndex = -1, string hitValue = null)
        {
            owner = screen;
            kind = hitKind;
            index = hitIndex;
            value = hitValue;
            EnsureCollider();
        }

        public void BindButton(Button button)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(InvokeTarget);
        }

        public void Interact(PointerSource source)
        {
            var button = GetComponent<Button>();
            if (button != null && !button.interactable) return;
            InvokeTarget();
        }

        private void InvokeTarget()
        {
            // A PICO hand pinch can surface both as its native aim-ray event and as an
            // Android pointer event.  They refer to one real-world pinch, so accepting both
            // would make a card toggle twice and visually look as if the click did nothing.
            if (Time.unscaledTime - lastDispatchTime < 0.16f) return;
            lastDispatchTime = Time.unscaledTime;
            Debug.Log($"[Guandan] 平面 UI 点击：{kind} / {index} / {value}");
            owner?.HandleTarget(kind, index, value);
        }

        private void EnsureCollider()
        {
            if (GetComponent<BoxCollider>() != null) return;
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 0.02f);
            collider.center = new Vector3(0f, 0f, 0.01f);
        }
    }

    /// <summary>Keeps legacy world labels readable when the desktop camera or headset turns.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        [SerializeField] private bool fullRotation = true;
        private Camera target;

        private void LateUpdate()
        {
            if (target == null) target = Camera.main;
            if (target == null) return;
            if (fullRotation)
            {
                transform.rotation = target.transform.rotation;
                return;
            }

            var direction = transform.position - target.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
