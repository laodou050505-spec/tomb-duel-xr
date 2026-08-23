using System;
using System.Linq;
using Guandan.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Guandan.UI
{
    /// <summary>
    /// World-space name/count/status plaque for an AI seat. The plaque follows the
    /// avatar's renderer bounds and billboards around the vertical axis toward the player.
    /// </summary>
    public sealed class WorldSeatStatusUi : MonoBehaviour
    {
        private const float CanvasWidth = 858f;
        private const float CanvasHeight = 370f;
        // The plaque is a real world-space surface.  Keep it large enough to read
        // comfortably in both PICO eyes instead of relying on a tiny camera HUD.
        private const float WorldScale = 0.00160f;
        private const float HeadGap = 0.20f;

        private GameDirector director;
        private ScreenGameUi screenUi;
        private PlayerSeat seat;
        private Transform fallbackAnchor;
        private Camera targetCamera;
        private Canvas canvas;
        private RectTransform canvasRect;
        private Text titleText;
        private Text statusText;
        private Sprite plaqueSprite;
        private Renderer[] avatarRenderers = Array.Empty<Renderer>();
        private Transform trackedAvatar;
        private bool initialized;

        public static WorldSeatStatusUi Create(
            GameDirector owner,
            ScreenGameUi screen,
            PlayerSeat targetSeat,
            Transform anchor,
            Sprite plaque)
        {
            var go = new GameObject($"WorldSeatStatus · {GuandanMatchEngine.SeatLabel(targetSeat)}");
            go.transform.SetParent(owner.transform.root, false);
            var view = go.AddComponent<WorldSeatStatusUi>();
            view.Configure(owner, screen, targetSeat, anchor, plaque);
            return view;
        }

        private void Configure(
            GameDirector owner,
            ScreenGameUi screen,
            PlayerSeat targetSeat,
            Transform anchor,
            Sprite plaque)
        {
            director = owner;
            screenUi = screen;
            seat = targetSeat;
            fallbackAnchor = anchor;
            plaqueSprite = plaque;
            targetCamera = Camera.main;
            BuildCanvas();
            initialized = true;
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject(
                "Spatial Plaque",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = targetCamera;
            canvas.sortingOrder = 80;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            canvasRect.localScale = Vector3.one * WorldScale;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var panel = canvasObject.AddComponent<Image>();
            panel.sprite = plaqueSprite;
            panel.type = Image.Type.Simple;
            panel.preserveAspect = true;
            panel.raycastTarget = false;

            titleText = CreateText(
                "Title",
                canvasRect,
                new Vector2(0f, 56f),
                new Vector2(730f, 144f),
                56,
                TextAnchor.MiddleCenter,
                new Color(0.96f, 0.88f, 0.70f));
            titleText.fontStyle = FontStyle.Bold;

            statusText = CreateText(
                "Status",
                canvasRect,
                new Vector2(0f, -86f),
                new Vector2(730f, 116f),
                42,
                TextAnchor.MiddleCenter,
                new Color(0.78f, 0.90f, 0.82f));
            statusText.fontStyle = FontStyle.Bold;

            // Keep the plaque selectable so the profile panel remains available after the
            // old camera-front profile buttons are hidden.
            var hitTarget = canvasObject.AddComponent<UiHitTarget>();
            hitTarget.Configure(screenUi, UiHitKind.Profile, (int)seat, null);
            var collider = canvasObject.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.size = new Vector3(CanvasWidth, CanvasHeight, 18f);
                collider.center = new Vector3(0f, 0f, 9f);
            }
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor anchor,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private void LateUpdate()
        {
            if (!initialized || director == null || canvas == null) return;
            if (targetCamera == null) targetCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            canvas.worldCamera = targetCamera;

            var visible = director.LotteryChosen && !director.RaceOpen && director.Match != null;
            if (canvas.gameObject.activeSelf != visible) canvas.gameObject.SetActive(visible);
            if (!visible) return;

            RefreshTargetAvatar();
            UpdatePanelPose();
            RefreshCopy();
        }

        private void RefreshTargetAvatar()
        {
            var next = director.GetAvatarTransform(seat);
            if (next == trackedAvatar) return;
            trackedAvatar = next;
            avatarRenderers = trackedAvatar != null
                ? trackedAvatar.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
        }

        private void UpdatePanelPose()
        {
            var position = fallbackAnchor != null
                ? fallbackAnchor.position + Vector3.up * 2.55f
                : transform.position;
            if (avatarRenderers.Length > 0)
            {
                var bounds = avatarRenderers[0].bounds;
                for (var i = 1; i < avatarRenderers.Length; i++) bounds.Encapsulate(avatarRenderers[i].bounds);
                position = new Vector3(bounds.center.x, bounds.max.y + HeadGap, bounds.center.z);
            }

            transform.position = position;
            if (targetCamera == null) return;
            var toCamera = targetCamera.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.0001f)
                toCamera = -targetCamera.transform.forward;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
                // World-space UGUI's readable face is its local -Z side.  Looking
                // directly at the camera therefore exposed the back of the plaque
                // and mirrored every glyph; turn the billboard around its vertical
                // axis while preserving the player's horizontal viewing direction.
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                    * Quaternion.Euler(0f, 180f, 0f);
        }

        private void RefreshCopy()
        {
            var match = director.Match;
            if (match == null) return;
            var profile = director.GetProfile(seat);
            var count = match.GetHand(seat).Count;
            var finishedIndex = match.FinishOrder.ToList().IndexOf(seat);
            var countText = finishedIndex >= 0
                ? $"第 {finishedIndex + 1} 名"
                : count > 10 ? "?张" : $"{count}张";
            titleText.text = $"{GuandanMatchEngine.SeatLabel(seat)} · {profile.Name}  {countText}";

            var play = director.GetSeatPlayLabel(seat);
            if (!string.IsNullOrEmpty(play))
            {
                var lines = play.Split('\n');
                var action = lines[0];
                var separator = action.LastIndexOf('·');
                if (separator >= 0 && separator + 1 < action.Length)
                    action = action[(separator + 1)..].Trim();
                statusText.text = $"当前 · {action}";
            }
            else if (match.ActiveSeat == seat && !director.IsPresentationLocked)
            {
                statusText.text = match.Phase == MatchPhase.TributeReturn ? "正在还贡…" : "思考中…";
            }
            else
            {
                statusText.text = "等待行动";
            }

            var teamColor = seat == PlayerSeat.North
                ? new Color(0.48f, 0.82f, 0.72f)
                : new Color(0.93f, 0.54f, 0.42f);
            titleText.color = teamColor;
        }
    }
}
