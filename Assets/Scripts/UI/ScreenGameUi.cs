using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Guandan.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Guandan.UI
{
    /// <summary>
    /// Camera-relative UI for the desktop fallback and PICO headset. The hand is always
    /// anchored to the lower edge of the player's view while every command faces the player.
    /// </summary>
    public sealed class ScreenGameUi : MonoBehaviour
    {
        private const float CanvasWidth = 1920f;
        private const float CanvasHeight = 1080f;
        // Keep the camera-front composition used by the original desktop build.
        // The panel is still parented to the head camera, so it remains available in XR.
        private static readonly Vector3 HeadsetUiOffset = new(0f, 0.08f, 1.70f);
        private const float HeadsetUiScale = 0.00164f;

        private GameDirector director;
        private Canvas canvas;
        private RectTransform root;
        private Camera targetCamera;
        private int lastReportedPhysicalCardCount = -1;
        private GameObject lotteryPanel;
        private GameObject tablePanel;
        private GameObject routePanel;
        private GameObject profilePanel;
        private GameObject variantPanel;
        private RectTransform handRoot;
        private RectTransform variantRoot;
        private Text titleText;
        private Text progressText;
        private Text turnText;
        private Text messageText;
        private Text blueLevelText;
        private Text redLevelText;
        private Text bluePositionText;
        private Text redPositionText;
        private Text lotteryResultText;
        private Text levelAnnouncementText;
        private GameObject levelAnnouncementPanel;
        private Text handInfoText;
        private Text selectionText;
        private Text tablePlayNorth;
        private Text tablePlayEast;
        private Text tablePlaySouth;
        private Text tablePlayWest;
        private RectTransform playCardsNorth;
        private RectTransform playCardsEast;
        private RectTransform playCardsSouth;
        private RectTransform playCardsWest;
        private RectTransform effectRoot;
        private Image[] headerBlueCells;
        private Image[] headerRedCells;
        private Image[] blueRouteCells;
        private Image[] redRouteCells;
        private Text routeSummaryText;
        private Text profileRoleText;
        private Text profileNameText;
        private Text profileTitleText;
        private Text profileBodyText;
        private Text profileTraitsText;
        private Text profileReactionText;
        private Text routeResultText;
        private Text routeTitleText;
        private Text routeAdviceText;
        private Text turnCalloutNorth;
        private Text turnCalloutEast;
        private Text turnCalloutSouth;
        private Text turnCalloutWest;
        private Button steadyButtonUi;
        private Button riskyButtonUi;
        private Button enterTreasureButtonUi;
        private Button continueButtonUi;
        private Button restartButtonUi;
        private Button playButtonUi;
        private Button passButtonUi;
        private Button profileNorthUi;
        private Button profileEastUi;
        private Button profileWestUi;
        private Button[] lotteryButtons;
        private bool built;
        private PlayerSeat profileSeat;
        private static Font uiFont;
        private static Sprite tombPanelSprite;
        private static Sprite tombButtonSprite;
        private static Sprite seatPlaqueSprite;
        private static Sprite scoreRailCapLeftSprite;
        private static Sprite scoreRailCapRightSprite;
        private static Sprite cardFaceSprite;
        private static Sprite bombRingSprite;
        private static Sprite bombSparkSprite;

        public Canvas Canvas => canvas;

        public void SetSpatialSeatPlaques(bool spatial)
        {
            SetProfileButtonVisible(profileNorthUi, !spatial);
            SetProfileButtonVisible(profileEastUi, !spatial);
            SetProfileButtonVisible(profileWestUi, !spatial);
        }

        private static void SetProfileButtonVisible(Button button, bool visible)
        {
            if (button != null && button.gameObject.activeSelf != visible)
                button.gameObject.SetActive(visible);
        }

        public void Initialize(GameDirector owner, Camera targetCamera)
        {
            director = owner;
            this.targetCamera = targetCamera != null ? targetCamera : Camera.main;
            EnsureCanvas(targetCamera);
            if (!built) Build();
            Refresh();
        }

        private void LateUpdate()
        {
            // GameDirector can awake before the XR rig has tagged its head camera as MainCamera.
            // Keep this flat HUD hidden until a head camera exists; it must never render at
            // world origin or become a row of apparent cards sitting on the physical table.
            if (canvas == null) return;
            if (targetCamera == null) targetCamera = ResolveTargetCamera();
            if (targetCamera == null)
            {
                if (canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
                return;
            }
            if (canvas.transform.parent != targetCamera.transform || !canvas.gameObject.activeSelf)
                AttachToCamera(targetCamera);
        }

        private void Start()
        {
            // A compact runtime invariant makes Player/Emulator evidence unambiguous: only
            // UGUI card buttons may exist in the flat-card presentation, never CardView meshes.
            var physicalCards = FindObjectsByType<CardView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (physicalCards == lastReportedPhysicalCardCount) return;
            lastReportedPhysicalCardCount = physicalCards;
            Debug.Log($"[Guandan] 平面牌 UI 运行态：物理 CardView={physicalCards}，Canvas父级={(canvas != null && canvas.transform.parent != null ? canvas.transform.parent.name : "未绑定")}。");
        }

        public void HandleTarget(UiHitKind kind, int index, string value)
        {
            if (director == null) return;
            switch (kind)
            {
                case UiHitKind.Card:
                    director.ToggleCardById(value);
                    break;
                case UiHitKind.Action:
                    switch (value)
                    {
                        case "play": director.TryPlaySelected(); break;
                        case "pass": director.HandleAction(GameAction.Pass); break;
                        case "hint": director.HandleAction(GameAction.Hint); break;
                        case "steady": director.HandleAction(GameAction.SteadyRoute); break;
                        case "risky": director.HandleAction(GameAction.RiskyRoute); break;
                        case "enter-treasure": director.EnterTreasureView(); break;
                        case "continue": director.HandleAction(GameAction.ContinueHand); break;
                        case "restart": director.HandleAction(GameAction.RestartMatch); break;
                        case "sound": director.ToggleSound(); break;
                        case "cancel-variant": HideVariantChoices(); break;
                        default:
                            if (value != null && value.StartsWith("variant:") && int.TryParse(value[8..], out var variantIndex))
                                director.ChooseVariant(variantIndex);
                            break;
                    }
                    break;
                case UiHitKind.Lottery:
                    director.ChooseLottery(index);
                    break;
                case UiHitKind.Profile:
                    ShowProfile((PlayerSeat)Mathf.Clamp(index, 0, 3));
                    break;
                case UiHitKind.CloseProfile:
                    profilePanel.SetActive(false);
                    break;
            }
            director.PlayUiClick();
            Refresh();
        }

        /// <summary>
        /// Resolves Android/PICO hand-pinch presses against the actual UGUI graphics.
        /// PICO Emulator forwards a pinch as a touch position, not as a physical controller
        /// trigger.  In an XR camera that position is not reliably equivalent to a physics
        /// ray, so do not use the decorative BoxCollider as the primary path here.
        /// </summary>
        public bool TryHandleScreenPress(Vector2 screenPosition, PointerSource source)
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return false;
            var eventSystem = EventSystem.current ?? FindFirstObjectByType<EventSystem>();
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (eventSystem == null || raycaster == null) return false;

            var eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
                button = PointerEventData.InputButton.Left,
            };
            var results = new List<RaycastResult>();
            raycaster.Raycast(eventData, results);
            foreach (var result in results)
            {
                var target = result.gameObject.GetComponentInParent<UiHitTarget>();
                if (target == null || !target.isActiveAndEnabled) continue;
                target.Interact(source);
                return true;
            }
            return false;
        }

        public void Refresh()
        {
            if (!built || director == null) return;
            var match = director.Match;
            lotteryPanel.SetActive(!director.LotteryChosen);
            tablePanel.SetActive(director.LotteryChosen && !director.RaceOpen);
            routePanel.SetActive(director.RaceOpen);
            if (director.RaceOpen)
            {
                profilePanel.SetActive(false);
                variantPanel.SetActive(false);
            }

            if (!director.LotteryChosen)
            {
                titleText.text = "先抽同行人";
                progressText.text = "三支身份签中，一支成为队友，其余两支成为对手";
                turnText.text = director.LotteryStatusMessage;
                lotteryResultText.text = director.LotteryStatusMessage;
                for (var i = 0; i < lotteryButtons.Length; i++)
                {
                    lotteryButtons[i].interactable = !director.LotteryBusy;
                    var label = lotteryButtons[i].GetComponentInChildren<Text>();
                    if (label != null) label.text = director.GetLotteryLotText(i);
                }
                return;
            }

            if (match == null) return;
            titleText.text = $"掼蛋夺宝 · 第 {match.HandNumber} 小局";
            blueLevelText.text = $"青队 · 打 {GuandanMatchEngine.RankLabel(match.GetTeamLevel(0))}";
            redLevelText.text = $"朱队 · 打 {GuandanMatchEngine.RankLabel(match.GetTeamLevel(1))}";
            bluePositionText.text = $"{director.Race.GetPosition(0)} / {TreasureRace.TrackLength}";
            redPositionText.text = $"{director.Race.GetPosition(1)} / {TreasureRace.TrackLength}";
            RenderRouteTrack(headerBlueCells, director.Race.GetPosition(0), new Color(0.20f, 0.66f, 0.58f));
            RenderRouteTrack(headerRedCells, director.Race.GetPosition(1), new Color(0.78f, 0.24f, 0.16f));
            progressText.text = $"本局级牌 {GuandanMatchEngine.RankLabel(match.LevelRank)}";
            turnText.text = match.Phase switch
            {
                MatchPhase.TributePayment => "正在进贡…",
                MatchPhase.TributeAssignment => "正在确认进贡归属…",
                MatchPhase.TributeReturn => match.ActiveSeat == PlayerSeat.South ? "请选择一张牌还贡" : "正在还贡…",
                MatchPhase.HandComplete => "本局名次已确认",
                MatchPhase.MatchComplete => "宝藏出土",
                _ => director.IsPresentationLocked ? director.PresentationMessage : string.Empty,
            };
            messageText.text = director.Message;
            SetButtonText("Sound", director.Muted ? "静音" : "声音");
            var humanTurn = !director.IsPresentationLocked && match.Phase == MatchPhase.Playing && match.ActiveSeat == PlayerSeat.South;
            playButtonUi.interactable = director.CanSubmitSelection;
            passButtonUi.interactable = humanTurn && match.CurrentPlay != null;
            SetButtonText("Play", match.Phase == MatchPhase.TributeReturn ? "还贡" : "出牌");
            levelAnnouncementPanel.SetActive(director.IsPresentationLocked && director.DealVisibleCount >= 27);
            levelAnnouncementText.text = director.PresentationMessage;
            RenderHand();
            RenderSeatPlaques();
            RenderTurnCallouts();
            RenderPlayZones();
            RenderRoute();
            RenderProfile();
        }

        public void ShowVariantChoices(IReadOnlyList<PlayPattern> candidates)
        {
            if (variantPanel == null || variantRoot == null) return;
            foreach (Transform child in variantRoot) Destroy(child.gameObject);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var label = $"{candidate.Label}\n{string.Join(" ", candidate.Cards.Select(card => card.ShortLabel))}";
                CreateButton($"Variant_{i}", new Vector2(0f, 90f - i * 94f), new Vector2(620f, 76f), label, new Color(0.23f, 0.34f, 0.29f), UiHitKind.Action, -1, $"variant:{i}");
            }
            variantPanel.SetActive(true);
        }

        public void HideVariantChoices()
        {
            if (variantPanel != null) variantPanel.SetActive(false);
        }

        public void PlayBombImpact(string label)
        {
            if (built) StartCoroutine(BombImpactRoutine(label));
        }

        public void PlayPlacementStamp(PlayerSeat seat, int place)
        {
            if (built) StartCoroutine(PlacementStampRoutine(seat, place));
        }

        public void ClearPlacementStamps()
        {
            if (effectRoot == null) return;
            foreach (Transform child in effectRoot)
            {
                if (child.name.StartsWith("Placement_", StringComparison.Ordinal)) Destroy(child.gameObject);
            }
        }

        public void PlayFlyCard(Card card, PlayerSeat from, PlayerSeat to, bool returning)
        {
            if (built && card != null) StartCoroutine(FlyCardRoutine(card, from, to, returning));
        }

        public void PlayFlyCardToCenter(Card card, PlayerSeat from)
        {
            if (built && card != null) StartCoroutine(FlyCardArcRoutine(card, SeatEffectPosition(from), Vector2.zero, "进贡", 0f));
        }

        public void PlayFlyCardFromCenter(Card card, PlayerSeat to, float delay)
        {
            if (built && card != null) StartCoroutine(FlyCardArcRoutine(card, Vector2.zero, SeatEffectPosition(to), "进贡", delay));
        }

        public void PlayTreasureMove(int team, int from, int to, bool chestOpened)
        {
            if (built) StartCoroutine(TreasureMoveRoutine(team, from, to, chestOpened));
        }

        private void EnsureCanvas(Camera targetCamera)
        {
            if (canvas != null) return;
            targetCamera ??= this.targetCamera != null ? this.targetCamera : Camera.main;
            this.targetCamera = targetCamera;
            var existing = targetCamera != null ? targetCamera.transform.Find("Screen UI · 始终面向玩家") : null;
            if (existing != null && existing.GetComponent<RectTransform>() == null)
            {
                Destroy(existing.gameObject);
                existing = null;
            }
            var go = existing != null
                ? existing.gameObject
                : new GameObject(
                    "Screen UI · 始终面向玩家",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
            go.SetActive(targetCamera != null);
            canvas = go.GetComponent<Canvas>();
            canvas.enabled = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            var raycaster = go.GetComponent<GraphicRaycaster>() ?? go.AddComponent<GraphicRaycaster>();
            // This canvas is parented in front of the head camera with identity rotation,
            // therefore the player sees its -Z side.  Filtering reversed graphics would make
            // every visible card look clickable while rejecting all touch hit tests.
            raycaster.ignoreReversedGraphics = false;
            var scaler = go.GetComponent<CanvasScaler>() ?? go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            root = rect;
            if (targetCamera != null) AttachToCamera(targetCamera);

            // The project uses Input System-only.  An EventSystem by itself can draw and
            // raycast a button but cannot turn Android/PICO pointer input into Button.onClick.
            // Add the matching module whether this is a newly-created EventSystem or one
            // already supplied by an additive scene.
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem · UI").AddComponent<EventSystem>();
            }
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                Debug.Log("[Guandan] 平面牌 UI 已接入 Input System EventSystem。");
            }
        }

        private void AttachToCamera(Camera camera)
        {
            if (camera == null || canvas == null) return;
            targetCamera = camera;
            var transform = canvas.transform;
            if (transform.parent != camera.transform) transform.SetParent(camera.transform, false);
            canvas.worldCamera = camera;
            // The UI stays flat, head-relative and visibly separate from the tomb models.
            transform.localPosition = HeadsetUiOffset;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one * HeadsetUiScale;
            if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        }

        private static Camera ResolveTargetCamera()
        {
            if (Camera.main != null) return Camera.main;
            return FindFirstObjectByType<Guandan.XR.GuandanXRBootstrap>()?.HeadCamera;
        }

        private void Build()
        {
            built = true;
            tombPanelSprite ??= Resources.Load<Sprite>("GuandanUI/TombPanel");
            tombButtonSprite ??= Resources.Load<Sprite>("GuandanUI/TombButton");
            seatPlaqueSprite ??= LoadSpriteResource("GuandanUI/SeatPlaque");
            scoreRailCapLeftSprite ??= LoadSpriteResource("GuandanUI/ScoreRailCapLeft");
            scoreRailCapRightSprite ??= LoadSpriteResource("GuandanUI/ScoreRailCapRight");
            cardFaceSprite ??= LoadSpriteResource("GuandanUI/CardFace");
            var backdrop = CreatePanel("UIBackdrop", root, new Vector2(0f, 0f), new Vector2(CanvasWidth, CanvasHeight), new Color(0.015f, 0.025f, 0.025f, 0.05f));
            backdrop.transform.SetAsFirstSibling();

            // Compose the rail from fixed-size decorative caps and a plain center band;
            // only the empty information area grows with the world-space HUD width.
            var header = CreatePanel("ScoreRail", root, new Vector2(0f, 454f), new Vector2(1872f, 148f), new Color(0.018f, 0.028f, 0.027f, 0.82f));
            var headerRect = header.transform as RectTransform;
            var headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.018f, 0.028f, 0.027f, 0.96f);
            headerImage.raycastTarget = false;
            var headerOutline = header.AddComponent<Outline>();
            headerOutline.effectColor = new Color(0.62f, 0.43f, 0.22f, 0.94f);
            headerOutline.effectDistance = new Vector2(2f, -2f);
            CreateHeaderRule(headerRect, new Vector2(0f, 59f));
            CreateHeaderRule(headerRect, new Vector2(0f, -59f));
            CreateHeaderCap(headerRect, "ScoreCapLeft", scoreRailCapLeftSprite, new Vector2(-842f, 0f));
            CreateHeaderCap(headerRect, "ScoreCapRight", scoreRailCapRightSprite, new Vector2(842f, 0f));
            var seal = CreatePanel("ScoreRailSeal", headerRect, Vector2.zero, new Vector2(46f, 38f), new Color(0.50f, 0.12f, 0.07f, 0.96f));
            seal.GetComponent<Image>().raycastTarget = false;
            var sealOutline = seal.AddComponent<Outline>();
            sealOutline.effectColor = new Color(0.78f, 0.58f, 0.28f, 0.92f);
            sealOutline.effectDistance = new Vector2(1f, -1f);
            titleText = CreateText("Title", "掼蛋夺宝", headerRect, new Vector2(-786f, 13f), 25, TextAnchor.MiddleLeft, new Color(0.94f, 0.88f, 0.68f));
            titleText.rectTransform.sizeDelta = new Vector2(330f, 54f);
            blueLevelText = CreateText("BlueLevel", "青队 · 打 2", headerRect, new Vector2(-440f, 24f), 19, TextAnchor.MiddleCenter, new Color(0.48f, 0.78f, 0.68f));
            redLevelText = CreateText("RedLevel", "朱队 · 打 2", headerRect, new Vector2(440f, 24f), 19, TextAnchor.MiddleCenter, new Color(0.88f, 0.46f, 0.36f));
            headerBlueCells = CreateHeaderTrack("HeaderBlue", headerRect, new Vector2(-440f, -18f), new Color(0.20f, 0.66f, 0.58f));
            headerRedCells = CreateHeaderTrack("HeaderRed", headerRect, new Vector2(440f, -18f), new Color(0.78f, 0.24f, 0.16f));
            bluePositionText = CreateText("BluePosition", $"0 / {TreasureRace.TrackLength}", headerRect, new Vector2(-440f, -43f), 14, TextAnchor.MiddleCenter, new Color(0.75f, 0.82f, 0.75f));
            redPositionText = CreateText("RedPosition", $"0 / {TreasureRace.TrackLength}", headerRect, new Vector2(440f, -43f), 14, TextAnchor.MiddleCenter, new Color(0.82f, 0.76f, 0.70f));
            progressText = CreateText("Progress", "等待开局", headerRect, new Vector2(0f, 3f), 21, TextAnchor.MiddleCenter, new Color(0.94f, 0.80f, 0.46f));
            progressText.rectTransform.sizeDelta = new Vector2(250f, 62f);
            turnText = CreateText("Turn", "", root, new Vector2(0f, 391f), 21, TextAnchor.MiddleCenter, new Color(0.82f, 0.90f, 0.82f));
            turnText.rectTransform.sizeDelta = new Vector2(820f, 44f);
            messageText = CreateText("Message", "", root, new Vector2(0f, 346f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.86f, 0.66f));
            messageText.rectTransform.sizeDelta = new Vector2(1120f, 36f);
            CreateButton("Sound", new Vector2(878f, 474f), new Vector2(116f, 62f), "声音", new Color(0.15f, 0.22f, 0.20f), UiHitKind.Action, -1, "sound");

            lotteryPanel = CreatePanel("LotteryPanel", root, new Vector2(0f, 30f), new Vector2(1120f, 570f), new Color(0.04f, 0.055f, 0.05f, 0.94f));
            ApplySkin(lotteryPanel.GetComponent<Image>(), tombPanelSprite, Color.white);
            CreateText("LotteryPrompt", "先抽同行人", lotteryPanel.transform as RectTransform, new Vector2(0f, 218f), 32, TextAnchor.MiddleCenter, new Color(0.94f, 0.80f, 0.46f));
            CreateText("LotteryHint", "三支身份签中，一支成为队友，其余两支成为对手", lotteryPanel.transform as RectTransform, new Vector2(0f, 171f), 17, TextAnchor.MiddleCenter, new Color(0.80f, 0.82f, 0.74f));
            lotteryButtons = new Button[3];
            for (var i = 0; i < lotteryButtons.Length; i++)
            {
                var button = CreateButton($"Lot_{i}", new Vector2(-260f + i * 260f, -12f), new Vector2(188f, 304f), director.GetLotteryLotText(i), Color.white, UiHitKind.Lottery, i, null);
                StyleLotteryCard(button, i);
                lotteryButtons[i] = button;
            }
            lotteryResultText = CreateText("LotteryResult", "选择一支竹签", lotteryPanel.transform as RectTransform, new Vector2(0f, -220f), 18, TextAnchor.MiddleCenter, new Color(0.92f, 0.82f, 0.57f));
            lotteryResultText.rectTransform.sizeDelta = new Vector2(960f, 58f);

            tablePanel = new GameObject("TablePanel");
            tablePanel.transform.SetParent(root, false);
            var tableRect = tablePanel.AddComponent<RectTransform>();
            tableRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            tablePlayNorth = CreateText("PlayNorth", "", tableRect, new Vector2(0f, 226f), 21, TextAnchor.MiddleCenter, Color.white);
            tablePlayEast = CreateText("PlayEast", "", tableRect, new Vector2(650f, 100f), 21, TextAnchor.MiddleCenter, Color.white);
            tablePlaySouth = CreateText("PlaySouth", "", tableRect, new Vector2(0f, -76f), 21, TextAnchor.MiddleCenter, Color.white);
            tablePlayWest = CreateText("PlayWest", "", tableRect, new Vector2(-650f, 100f), 21, TextAnchor.MiddleCenter, Color.white);
            tablePlayNorth.rectTransform.sizeDelta = new Vector2(640f, 38f);
            tablePlayEast.rectTransform.sizeDelta = new Vector2(560f, 38f);
            tablePlaySouth.rectTransform.sizeDelta = new Vector2(640f, 38f);
            tablePlayWest.rectTransform.sizeDelta = new Vector2(560f, 38f);
            playCardsNorth = CreateRect("PlayCardsNorth", tableRect, new Vector2(0f, 166f), new Vector2(640f, 96f));
            playCardsEast = CreateRect("PlayCardsEast", tableRect, new Vector2(650f, 42f), new Vector2(560f, 96f));
            playCardsSouth = CreateRect("PlayCardsSouth", tableRect, new Vector2(0f, -136f), new Vector2(640f, 96f));
            playCardsWest = CreateRect("PlayCardsWest", tableRect, new Vector2(-650f, 42f), new Vector2(560f, 96f));
            handInfoText = CreateText("HandInfo", "", tableRect, new Vector2(-758f, -190f), 18, TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.76f));
            handInfoText.rectTransform.sizeDelta = new Vector2(520f, 48f);
            selectionText = CreateText("Selection", "", tableRect, new Vector2(758f, -190f), 18, TextAnchor.MiddleRight, new Color(0.85f, 0.85f, 0.76f));
            selectionText.rectTransform.sizeDelta = new Vector2(650f, 48f);
            handRoot = CreateRect("HandRoot", tableRect, new Vector2(0f, -330f), new Vector2(1740f, 270f));
            handRoot.SetAsLastSibling();
            passButtonUi = CreateButton("Pass", new Vector2(-105f, -490f), new Vector2(190f, 82f), "过牌", new Color(0.18f, 0.34f, 0.31f), UiHitKind.Action, -1, "pass");
            playButtonUi = CreateButton("Play", new Vector2(105f, -490f), new Vector2(190f, 82f), "出牌", new Color(0.62f, 0.20f, 0.12f), UiHitKind.Action, -1, "play");

            profileNorthUi = CreateButton("ProfileNorth", new Vector2(0f, 286f), new Vector2(278f, 120f), "北家", new Color(0.12f, 0.20f, 0.18f), UiHitKind.Profile, 2, null);
            profileEastUi = CreateButton("ProfileEast", new Vector2(770f, 184f), new Vector2(255f, 110f), "东家", new Color(0.17f, 0.13f, 0.13f), UiHitKind.Profile, 1, null);
            profileWestUi = CreateButton("ProfileWest", new Vector2(-770f, 184f), new Vector2(255f, 110f), "西家", new Color(0.17f, 0.13f, 0.13f), UiHitKind.Profile, 3, null);
            turnCalloutNorth = CreateText("TurnNorth", "", tableRect, new Vector2(-390f, 296f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.48f));
            turnCalloutEast = CreateText("TurnEast", "", tableRect, new Vector2(770f, 108f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.48f));
            turnCalloutWest = CreateText("TurnWest", "", tableRect, new Vector2(-770f, 108f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.48f));
            turnCalloutSouth = CreateText("TurnSouth", "", tableRect, new Vector2(0f, -178f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.48f));
            turnCalloutNorth.rectTransform.sizeDelta = new Vector2(320f, 38f);
            turnCalloutEast.rectTransform.sizeDelta = new Vector2(300f, 38f);
            turnCalloutWest.rectTransform.sizeDelta = new Vector2(300f, 38f);
            turnCalloutSouth.rectTransform.sizeDelta = new Vector2(300f, 38f);

            levelAnnouncementPanel = CreatePanel("LevelAnnouncement", tableRect, new Vector2(0f, 32f), new Vector2(660f, 230f), new Color(0.07f, 0.08f, 0.065f, 0.96f));
            ApplySkin(levelAnnouncementPanel.GetComponent<Image>(), tombPanelSprite, Color.white);
            levelAnnouncementText = CreateText("LevelAnnouncementText", "", levelAnnouncementPanel.transform, Vector2.zero, 31, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.48f));
            levelAnnouncementText.rectTransform.sizeDelta = new Vector2(620f, 190f);

            routePanel = CreatePanel("RoutePanel", root, new Vector2(0f, -366f), new Vector2(1880f, 250f), new Color(0.045f, 0.055f, 0.05f, 0.0f));
            // Treasure information now lives on the two authored flags. This layer is a
            // transparent command rail at the bottom of the view, so it never masks the
            // room, chest, artifacts, or moving pieces.
            var routeImage = routePanel.GetComponent<Image>();
            ApplySkin(routeImage, tombPanelSprite, Color.white);
            routeImage.color = new Color(1f, 1f, 1f, 0f);
            routeTitleText = CreateText("RouteTitle", "选择发掘路线", routePanel.transform as RectTransform, new Vector2(0f, 102f), 24, TextAnchor.MiddleCenter, new Color(0.94f, 0.80f, 0.46f));
            routeSummaryText = CreateText("RouteSummary", "", routePanel.transform as RectTransform, new Vector2(0f, 73f), 15, TextAnchor.MiddleCenter, new Color(0.85f, 0.84f, 0.75f));
            routeAdviceText = CreateText("RouteAdvice", "", routePanel.transform as RectTransform, new Vector2(0f, 46f), 14, TextAnchor.MiddleCenter, new Color(0.76f, 0.84f, 0.76f));
            blueRouteCells = CreateRouteTrack("BlueRoute", routePanel.transform, new Vector2(-310f, 12f), new Color(0.20f, 0.66f, 0.58f));
            redRouteCells = CreateRouteTrack("RedRoute", routePanel.transform, new Vector2(310f, 12f), new Color(0.78f, 0.24f, 0.16f));
            enterTreasureButtonUi = CreateButton("EnterTreasure", new Vector2(0f, -30f), new Vector2(280f, 52f), "进入夺宝场景", new Color(0.70f, 0.42f, 0.16f), UiHitKind.Action, -1, "enter-treasure");
            steadyButtonUi = CreateButton("Steady", new Vector2(-180f, -89f), new Vector2(280f, 56f), "稳当推进", new Color(0.22f, 0.38f, 0.32f), UiHitKind.Action, -1, "steady");
            riskyButtonUi = CreateButton("Risky", new Vector2(180f, -89f), new Vector2(280f, 56f), "深入探方", new Color(0.43f, 0.22f, 0.16f), UiHitKind.Action, -1, "risky");
            routeResultText = CreateText("RouteResult", "", routePanel.transform as RectTransform, new Vector2(0f, -145f), 18, TextAnchor.MiddleCenter, new Color(0.96f, 0.82f, 0.50f));
            routeResultText.rectTransform.sizeDelta = new Vector2(980f, 82f);
            continueButtonUi = CreateButton("Continue", new Vector2(0f, -224f), new Vector2(280f, 58f), "继续下一局", new Color(0.18f, 0.28f, 0.25f), UiHitKind.Action, -1, "continue");
            restartButtonUi = CreateButton("Restart", new Vector2(0f, -224f), new Vector2(240f, 58f), "重新抽签", new Color(0.34f, 0.21f, 0.13f), UiHitKind.Action, -1, "restart");

            profilePanel = CreatePanel("ProfilePanel", root, new Vector2(0f, 15f), new Vector2(760f, 640f), new Color(0.04f, 0.055f, 0.05f, 0.98f));
            ApplySkin(profilePanel.GetComponent<Image>(), tombPanelSprite, Color.white);
            profileRoleText = CreateText("ProfileRole", "", profilePanel.transform as RectTransform, new Vector2(0f, 235f), 18, TextAnchor.MiddleCenter, new Color(0.78f, 0.78f, 0.70f));
            profileNameText = CreateText("ProfileName", "", profilePanel.transform as RectTransform, new Vector2(0f, 180f), 34, TextAnchor.MiddleCenter, new Color(0.94f, 0.80f, 0.46f));
            profileTitleText = CreateText("ProfileTitle", "", profilePanel.transform as RectTransform, new Vector2(0f, 130f), 21, TextAnchor.MiddleCenter, Color.white);
            profileBodyText = CreateText("ProfileBody", "", profilePanel.transform as RectTransform, new Vector2(0f, 45f), 18, TextAnchor.MiddleCenter, new Color(0.82f, 0.83f, 0.76f));
            profileTraitsText = CreateText("ProfileTraits", "", profilePanel.transform as RectTransform, new Vector2(0f, -70f), 18, TextAnchor.MiddleCenter, new Color(0.78f, 0.86f, 0.77f));
            profileReactionText = CreateText("ProfileReaction", "", profilePanel.transform as RectTransform, new Vector2(0f, -155f), 18, TextAnchor.MiddleCenter, new Color(0.92f, 0.78f, 0.60f));
            profileRoleText.rectTransform.sizeDelta = new Vector2(600f, 38f);
            profileNameText.rectTransform.sizeDelta = new Vector2(600f, 54f);
            profileTitleText.rectTransform.sizeDelta = new Vector2(600f, 44f);
            profileBodyText.rectTransform.sizeDelta = new Vector2(600f, 104f);
            profileTraitsText.rectTransform.sizeDelta = new Vector2(600f, 72f);
            profileReactionText.rectTransform.sizeDelta = new Vector2(600f, 72f);
            CreateButton("ProfileClose", new Vector2(0f, -240f), new Vector2(220f, 58f), "关闭人物资料", new Color(0.20f, 0.24f, 0.22f), UiHitKind.CloseProfile);

            variantPanel = CreatePanel("VariantPanel", root, new Vector2(0f, 25f), new Vector2(760f, 620f), new Color(0.04f, 0.055f, 0.05f, 0.98f));
            ApplySkin(variantPanel.GetComponent<Image>(), tombPanelSprite, Color.white);
            var variantTitle = CreateText("VariantTitle", "选择逢人配的牌型解释", variantPanel.transform as RectTransform, new Vector2(0f, 245f), 28, TextAnchor.MiddleCenter, new Color(0.94f, 0.80f, 0.46f));
            variantTitle.rectTransform.sizeDelta = new Vector2(620f, 60f);
            variantRoot = CreateRect("VariantRoot", variantPanel.transform, new Vector2(0f, 15f), new Vector2(680f, 420f));
            CreateButton("VariantClose", new Vector2(0f, -255f), new Vector2(210f, 52f), "取消", new Color(0.20f, 0.24f, 0.22f), UiHitKind.Action, -1, "cancel-variant");

            effectRoot = CreateRect("PlayerFacingEffects", root, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight));
            effectRoot.SetAsLastSibling();

            lotteryPanel.SetActive(true);
            tablePanel.SetActive(false);
            routePanel.SetActive(false);
            profilePanel.SetActive(false);
            variantPanel.SetActive(false);
            levelAnnouncementPanel.SetActive(false);
            enterTreasureButtonUi.gameObject.SetActive(false);
        }

        private void RenderHand()
        {
            if (handRoot == null) return;
            foreach (Transform child in handRoot) Destroy(child.gameObject);
            var match = director.Match;
            if (match == null) return;
            var allCards = match.GetHand(PlayerSeat.South);
            var visibleCount = director.IsPresentationLocked
                ? Mathf.Clamp(director.DealVisibleCount, 0, allCards.Count)
                : allCards.Count;
            var cards = allCards.Take(visibleCount).ToArray();
            var singleRow = cards.Length <= MaxSingleRowCards();
            var firstRow = singleRow ? cards.Length : Mathf.CeilToInt(cards.Length * 0.5f);
            for (var index = 0; index < cards.Length; index++)
            {
                var row = index < firstRow ? 0 : 1;
                var column = row == 0 ? index : index - firstRow;
                var rowCount = row == 0 ? firstRow : cards.Length - firstRow;
                var card = cards[index];
                var selected = director.IsCardSelected(card.Id);
                var hinted = director.IsHinted(card.Id);
                var x = (column - (rowCount - 1) * 0.5f) * HandCardStep;
                var y = (singleRow ? 0f : row == 0 ? 67f : -72f) + (selected ? 18f : 0f);
                var button = CreateCardButton(card, handRoot, new Vector2(x, y));
                var legal = director.IsCardSelectable(card.Id);
                button.interactable = legal;
                var label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = CardLabel(card, match.LevelRank);
                    label.color = SuitColor(card);
                }
                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected
                        ? new Color(1.00f, 0.84f, 0.52f, 1f)
                        : legal ? Color.white : new Color(0.68f, 0.66f, 0.58f, 1f);
                    var outline = button.gameObject.AddComponent<Outline>();
                    outline.effectDistance = selected ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
                    outline.effectColor = selected
                        ? new Color(0.94f, 0.58f, 0.12f, 0.98f)
                        : hinted ? new Color(0.72f, 0.12f, 0.07f, 0.98f) : new Color(0.18f, 0.17f, 0.13f, 0.78f);
                    if (hinted)
                    {
                        var mark = CreatePanel("MinimumBeat", button.transform, new Vector2(35f, 48f), new Vector2(13f, 13f), new Color(0.76f, 0.10f, 0.06f, 1f));
                        mark.GetComponent<Image>().raycastTarget = false;
                    }
                }
            }

            var placement = -1;
            for (var index = 0; index < match.FinishOrder.Count; index++)
            {
                if (match.FinishOrder[index] == PlayerSeat.South) placement = index + 1;
            }
            handInfoText.text = placement > 0
                ? $"你 · 第 {placement} 名"
                : director.IsPresentationLocked ? $"正在发牌 {visibleCount} / 27" : $"手牌 {allCards.Count}";
            if (director.IsPresentationLocked)
                selectionText.text = director.DealVisibleCount >= 27 ? "正在揭示本局级牌" : "牌正在依次到手";
            else if (match.Phase == MatchPhase.TributeReturn && match.ActiveSeat == PlayerSeat.South)
                selectionText.text = director.SelectedCount == 1 ? "确认要还出的牌" : "请选择一张牌还贡";
            else if (director.SelectedCount > 0)
                selectionText.text = director.CanSubmitSelection ? $"已选 {director.SelectedCount} 张 · 可以出牌" : $"已选 {director.SelectedCount} 张 · 牌型不成立";
            else
                selectionText.text = match.Phase == MatchPhase.Playing && match.ActiveSeat == PlayerSeat.South ? "请选择要出的牌" : "等待其他牌手";
        }

        private const float HandCardWidth = 90f;
        private const float HandCardStep = 102f;

        private int MaxSingleRowCards()
        {
            var width = handRoot != null && handRoot.sizeDelta.x > 0f ? handRoot.sizeDelta.x : 1740f;
            return Mathf.Max(1, Mathf.FloorToInt((width - HandCardWidth) / HandCardStep) + 1);
        }

        private void RenderSeatPlaques()
        {
            var match = director.Match;
            if (match == null) return;
            SetButtonText("ProfileNorth", $"北 · {director.GetProfile(PlayerSeat.North).Name}  {SeatCount(PlayerSeat.North)}");
            SetButtonText("ProfileEast", $"东 · {director.GetProfile(PlayerSeat.East).Name}  {SeatCount(PlayerSeat.East)}");
            SetButtonText("ProfileWest", $"西 · {director.GetProfile(PlayerSeat.West).Name}  {SeatCount(PlayerSeat.West)}");
        }

        private void RenderTurnCallouts()
        {
            turnCalloutNorth.text = string.Empty;
            turnCalloutEast.text = string.Empty;
            turnCalloutSouth.text = string.Empty;
            turnCalloutWest.text = string.Empty;
            if (director.IsPresentationLocked || director.Match == null) return;
            var match = director.Match;
            var value = match.Phase switch
            {
                MatchPhase.Playing => match.ActiveSeat == PlayerSeat.South ? "轮到你出牌" : "思考中…",
                MatchPhase.TributePayment => "正在进贡…",
                MatchPhase.TributeAssignment => "确认进贡归属…",
                MatchPhase.TributeReturn => match.ActiveSeat == PlayerSeat.South ? "请选择还贡牌" : "正在还贡…",
                _ => string.Empty,
            };
            if (string.IsNullOrEmpty(value)) return;
            switch (match.ActiveSeat)
            {
                case PlayerSeat.South: turnCalloutSouth.text = value; break;
                case PlayerSeat.East: turnCalloutEast.text = value; break;
                case PlayerSeat.North: turnCalloutNorth.text = value; break;
                case PlayerSeat.West: turnCalloutWest.text = value; break;
            }
        }

        private void RenderPlayZones()
        {
            RenderPlayZone(PlayerSeat.North, tablePlayNorth, playCardsNorth);
            RenderPlayZone(PlayerSeat.East, tablePlayEast, playCardsEast);
            RenderPlayZone(PlayerSeat.South, tablePlaySouth, playCardsSouth);
            RenderPlayZone(PlayerSeat.West, tablePlayWest, playCardsWest);
        }

        private void RenderRoute()
        {
            if (!director.RaceOpen || director.Match?.LastHandResult == null) return;
            var result = director.Match.LastHandResult;
            var winningTeamName = result.WinningTeam == 0 ? "青队" : "朱队";
            var firstName = result.FirstSeat == PlayerSeat.South ? "你" : director.GetProfile(result.FirstSeat).Name;
            routeSummaryText.text = $"本局发掘记录 · {winningTeamName} {FinishCopy(result)} · 基础前进 {result.BaseSteps} 格";
            routeResultText.text = director.RouteResultMessage;
            RenderRouteTrack(blueRouteCells, director.Race.GetPosition(0), new Color(0.20f, 0.66f, 0.58f));
            RenderRouteTrack(redRouteCells, director.Race.GetPosition(1), new Color(0.78f, 0.24f, 0.16f));
            var humanChoice = result.WinningTeam == 0 && !director.RaceResolved;
            var viewReady = director.TreasureViewActive;
            var transitionOnly = !viewReady && !director.RaceResolved;
            enterTreasureButtonUi.gameObject.SetActive(transitionOnly);
            enterTreasureButtonUi.interactable = transitionOnly;
            routeSummaryText.gameObject.SetActive(!transitionOnly);
            routeAdviceText.gameObject.SetActive(!transitionOnly);
            routeResultText.gameObject.SetActive(!transitionOnly);
            SetRouteTrackVisible(blueRouteCells, !transitionOnly);
            SetRouteTrackVisible(redRouteCells, !transitionOnly);
            steadyButtonUi.gameObject.SetActive(viewReady && !director.RaceResolved);
            riskyButtonUi.gameObject.SetActive(viewReady && !director.RaceResolved);
            routeTitleText.rectTransform.anchoredPosition = new Vector2(0f, transitionOnly ? 38f : 102f);
            enterTreasureButtonUi.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -42f);
            steadyButtonUi.interactable = humanChoice && viewReady;
            riskyButtonUi.interactable = humanChoice && viewReady;
            SetButtonText("Steady", $"稳当推进\n确定前进 {result.BaseSteps} 格");
            SetButtonText("Risky", "深入探方\n翻倍 10% · +1 格 15% · 正常 55%\n少 1 格 12% · 倒退 1 格 8%");
            if (!director.RaceResolved)
            {
                routeTitleText.text = viewReady ? (humanChoice ? "选择发掘路线" : $"{firstName}正在选择探方") : "本局结算 · 前往夺宝";
                routeAdviceText.text = humanChoice
                    ? (viewReady
                        ? (director.GetProfile(PlayerSeat.North).RiskBias > 0.5f ? "队友建议：可以深入探一步。" : "队友建议：先保住这次发现。")
                        : "点击上方按钮切换到夺宝路线视角。")
                    : (viewReady ? $"{director.GetProfile(result.FirstSeat).Title}会结合当前位置决定是否深入。" : "等待切换到夺宝路线视角…");
                routeResultText.text = humanChoice && viewReady ? string.Empty : (viewReady ? "等待对手队选择…" : string.Empty);
            }
            else if (director.Race.IsComplete)
            {
                routeTitleText.text = $"{(director.Race.WinningTeam == 0 ? "青队取得宝藏" : "朱队取得宝藏")}";
                routeAdviceText.text = "中央出土宝箱已经开启。";
            }
            else
            {
                routeTitleText.text = "本次推进完成";
                routeAdviceText.text = "宝箱仍在地宫深处，继续下一局。";
            }
            continueButtonUi.gameObject.SetActive(director.RaceResolved && !director.Race.IsComplete);
            restartButtonUi.gameObject.SetActive(director.RaceResolved && director.Race.IsComplete);
            continueButtonUi.interactable = director.RaceResolved && !director.Race.IsComplete;
        }

        private static void SetRouteTrackVisible(Image[] cells, bool visible)
        {
            if (cells == null || cells.Length == 0 || cells[0] == null) return;
            var track = cells[0].transform.parent;
            if (track != null) track.gameObject.SetActive(visible);
        }

        private static string FinishCopy(HandResult result)
        {
            var partnerIndex = result.Placements.ToList().IndexOf(GuandanMatchEngine.PartnerOf(result.FirstSeat));
            return partnerIndex switch
            {
                1 => "头游 · 二游",
                2 => "头游 · 三游",
                _ => "头游 · 末游",
            };
        }

        private void RenderPlayZone(PlayerSeat seat, Text title, RectTransform cardRoot)
        {
            if (title == null || cardRoot == null) return;
            foreach (Transform child in cardRoot) Destroy(child.gameObject);
            var value = director.GetSeatPlayLabel(seat) ?? string.Empty;
            var lineBreak = value.IndexOf('\n');
            var cards = director.GetSeatPlayCards(seat);
            if (cards == null || cards.Count == 0)
            {
                title.text = lineBreak >= 0 ? value[(lineBreak + 1)..] : value;
                return;
            }
            title.text = lineBreak >= 0 ? value[..lineBreak] : value;
            var step = cards.Count > 8 ? 41f : 56f;
            for (var index = 0; index < cards.Count; index++)
            {
                var x = (index - (cards.Count - 1) * 0.5f) * step;
                CreateMiniCardGraphic(cards[index], cardRoot, new Vector2(x, 0f), new Vector2(52f, 80f));
            }
        }

        private Image[] CreateHeaderTrack(string name, Transform parent, Vector2 position, Color activeColor)
        {
            var track = CreateRect(name, parent, position, new Vector2(310f, 24f));
            var cells = new Image[TreasureRace.TrackLength];
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = CreatePanel($"Step_{index + 1:00}", track, new Vector2(-135f + index * 30f, 0f), new Vector2(23f, 10f), new Color(0.12f, 0.14f, 0.13f, 0.92f));
                cells[index] = cell.GetComponent<Image>();
                cells[index].raycastTarget = false;
            }
            return cells;
        }

        private static void CreateHeaderRule(Transform parent, Vector2 position)
        {
            var rule = CreatePanelStatic("HeaderRule", parent, position, new Vector2(1570f, 3f), new Color(0.67f, 0.47f, 0.24f, 0.82f));
            rule.GetComponent<Image>().raycastTarget = false;
        }

        private static void CreateHeaderCap(Transform parent, string name, Sprite sprite, Vector2 position)
        {
            var cap = CreatePanelStatic(name, parent, position, new Vector2(154f, 138f), Color.white);
            var image = cap.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static GameObject CreatePanelStatic(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.color = color;
            return go;
        }

        private Image[] CreateRouteTrack(string name, Transform parent, Vector2 position, Color activeColor)
        {
            var track = CreateRect(name, parent, position, new Vector2(560f, 30f));
            var label = CreateText("Label", name.StartsWith("Blue") ? "青" : "朱", track, new Vector2(-265f, 0f), 16, TextAnchor.MiddleCenter, activeColor);
            label.rectTransform.sizeDelta = new Vector2(36f, 28f);
            var cells = new Image[TreasureRace.TrackLength];
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = CreatePanel($"Step_{index + 1:00}", track, new Vector2(-210f + index * 47f, 0f), new Vector2(39f, 22f), new Color(0.12f, 0.14f, 0.13f, 0.88f));
                cells[index] = cell.GetComponent<Image>();
                cells[index].raycastTarget = false;
                var number = CreateText("Number", (index + 1).ToString(), cell.transform, Vector2.zero, 11, TextAnchor.MiddleCenter, Color.white);
                number.rectTransform.sizeDelta = new Vector2(39f, 22f);
            }
            return cells;
        }

        private static void RenderRouteTrack(Image[] cells, int position, Color activeColor)
        {
            if (cells == null) return;
            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index] == null) continue;
                cells[index].color = index < position
                    ? activeColor
                    : index == position && position < cells.Length
                        ? Color.Lerp(activeColor, Color.white, 0.22f)
                        : new Color(0.12f, 0.14f, 0.13f, 0.88f);
            }
        }

        private RectTransform CreateMiniCardGraphic(Card card, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject($"Mini_{card.Id}");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.sprite = cardFaceSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            var fontSize = Mathf.Clamp(Mathf.RoundToInt(size.y * 0.19f), 12, 20);
            var label = CreateText("Label", CardLabel(card, director.Match.LevelRank), rect, Vector2.zero, fontSize, TextAnchor.MiddleCenter, SuitColor(card));
            label.rectTransform.sizeDelta = size - new Vector2(4f, 4f);
            return rect;
        }

        private IEnumerator BombImpactRoutine(string label)
        {
            EnsureBombSprites();
            var panel = CreatePanel("BombImpact", effectRoot, Vector2.zero, new Vector2(CanvasWidth, CanvasHeight), new Color(0.42f, 0.025f, 0.012f, 0.14f));
            panel.GetComponent<Image>().raycastTarget = false;
            var group = panel.AddComponent<CanvasGroup>();
            var ringA = CreateBombRing(panel.transform, new Color(0.94f, 0.30f, 0.10f, 0.92f), 0.2f);
            var ringB = CreateBombRing(panel.transform, new Color(1.00f, 0.76f, 0.28f, 0.76f), 0.2f);
            var ringC = CreateBombRing(panel.transform, new Color(0.52f, 0.84f, 0.70f, 0.58f), 0.2f);
            var stampPanel = CreatePanel("VermilionSeal", panel.transform, Vector2.zero, new Vector2(470f, 188f), new Color(0.46f, 0.055f, 0.028f, 0.94f));
            var stampImage = stampPanel.GetComponent<Image>();
            ApplySkin(stampImage, tombButtonSprite, Color.white);
            stampImage.color = new Color(0.58f, 0.10f, 0.055f, 0.94f);
            stampImage.raycastTarget = false;
            var stamp = CreateText("Seal", $"炸弹\n{label}", stampPanel.transform, Vector2.zero, 42, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.45f));
            stamp.rectTransform.sizeDelta = new Vector2(430f, 168f);
            stamp.fontStyle = FontStyle.Bold;
            var sparks = new Image[12];
            var sparkDirections = new Vector2[12];
            for (var index = 0; index < sparks.Length; index++)
            {
                var angle = (index / (float)sparks.Length) * Mathf.PI * 2f + 0.16f;
                sparkDirections[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var spark = new GameObject($"BombSpark_{index:00}", typeof(RectTransform), typeof(Image));
                spark.transform.SetParent(panel.transform, false);
                var sparkRect = spark.transform as RectTransform;
                sparkRect.sizeDelta = new Vector2(index % 3 == 0 ? 24f : 14f, index % 3 == 0 ? 24f : 14f);
                var sparkImage = spark.GetComponent<Image>();
                sparkImage.sprite = bombSparkSprite;
                sparkImage.color = index % 2 == 0 ? new Color(1f, 0.77f, 0.28f, 0.95f) : new Color(0.36f, 0.76f, 0.65f, 0.88f);
                sparkImage.raycastTarget = false;
                sparks[index] = sparkImage;
            }

            for (var time = 0f; time < 1.12f; time += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(time / 1.12f);
                var burst = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 1.5f));
                ringA.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.95f, burst);
                ringB.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1.45f, Mathf.Clamp01(progress * 1.9f));
                ringC.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.05f, 2.55f, Mathf.Clamp01(progress * 1.25f));
                SetAlpha(ringA, Mathf.Lerp(0.95f, 0f, progress));
                SetAlpha(ringB, Mathf.Lerp(0.78f, 0f, Mathf.Clamp01(progress * 1.2f)));
                SetAlpha(ringC, Mathf.Lerp(0.52f, 0f, Mathf.Clamp01(progress * 1.1f)));
                stampPanel.transform.localScale = Vector3.one * Mathf.Lerp(0.62f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, progress * 3f)));
                for (var index = 0; index < sparks.Length; index++)
                {
                    var sparkRect = sparks[index].rectTransform;
                    sparkRect.anchoredPosition = sparkDirections[index] * Mathf.Lerp(18f, 360f, burst);
                    sparkRect.localRotation = Quaternion.Euler(0f, 0f, time * (index % 2 == 0 ? 210f : -180f));
                    SetAlpha(sparks[index], Mathf.Lerp(0.95f, 0f, Mathf.Clamp01(progress * 1.15f)));
                }
                panel.transform.localPosition = new Vector3(Mathf.Sin(time * 48f) * Mathf.Lerp(0f, 5f, 1f - progress), Mathf.Cos(time * 53f) * Mathf.Lerp(0f, 3f, 1f - progress), 0f);
                group.alpha = progress < 0.72f ? 1f : 1f - (progress - 0.72f) / 0.28f;
                yield return null;
            }
            effectRoot.localPosition = Vector3.zero;
            Destroy(panel);
        }

        private static void EnsureBombSprites()
        {
            if (bombRingSprite != null && bombSparkSprite != null) return;
            const int ringSize = 128;
            var ringTexture = new Texture2D(ringSize, ringSize, TextureFormat.RGBA32, false);
            ringTexture.filterMode = FilterMode.Bilinear;
            ringTexture.wrapMode = TextureWrapMode.Clamp;
            var center = (ringSize - 1) * 0.5f;
            for (var y = 0; y < ringSize; y++)
            {
                for (var x = 0; x < ringSize; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var edge = 1f - Mathf.Clamp01(Mathf.Abs(distance - 53f) / 5f);
                    ringTexture.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge));
                }
            }
            ringTexture.Apply();
            bombRingSprite = Sprite.Create(ringTexture, new Rect(0f, 0f, ringSize, ringSize), new Vector2(0.5f, 0.5f), 100f);

            const int sparkSize = 32;
            var sparkTexture = new Texture2D(sparkSize, sparkSize, TextureFormat.RGBA32, false);
            sparkTexture.filterMode = FilterMode.Bilinear;
            sparkTexture.wrapMode = TextureWrapMode.Clamp;
            var sparkCenter = (sparkSize - 1) * 0.5f;
            for (var y = 0; y < sparkSize; y++)
            {
                for (var x = 0; x < sparkSize; x++)
                {
                    var distance = Mathf.Abs(x - sparkCenter) + Mathf.Abs(y - sparkCenter);
                    var edge = 1f - Mathf.Clamp01((distance - 8f) / 3f);
                    sparkTexture.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge));
                }
            }
            sparkTexture.Apply();
            bombSparkSprite = Sprite.Create(sparkTexture, new Rect(0f, 0f, sparkSize, sparkSize), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Image CreateBombRing(Transform parent, Color color, float scale)
        {
            var go = new GameObject("BombShockRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.transform as RectTransform;
            rect.sizeDelta = new Vector2(330f, 330f);
            rect.localScale = Vector3.one * scale;
            var image = go.GetComponent<Image>();
            image.sprite = bombRingSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            var color = image.color;
            color.a = alpha;
            image.color = color;
        }

        private IEnumerator PlacementStampRoutine(PlayerSeat seat, int place)
        {
            if (effectRoot == null) yield break;
            var color = GuandanMatchEngine.TeamOf(seat) == 0
                ? new Color(0.12f, 0.50f, 0.44f, 0.94f)
                : new Color(0.64f, 0.16f, 0.10f, 0.94f);
            var panel = CreatePanel($"Placement_{seat}", effectRoot, SeatEffectPosition(seat), new Vector2(278f, 120f), Color.white);
            var panelImage = panel.GetComponent<Image>();
            ApplyAspectSkin(panelImage, seatPlaqueSprite, Color.white);
            panelImage.raycastTarget = false;
            var group = panel.AddComponent<CanvasGroup>();
            var accent = CreatePanelStatic("TeamAccent", panel.transform, new Vector2(0f, -40f), new Vector2(112f, 3f), color);
            accent.GetComponent<Image>().raycastTarget = false;
            var label = CreateText("Label", $"{SeatName(seat)}\n第 {place} 名", panel.transform, new Vector2(0f, -2f), 23, TextAnchor.MiddleCenter, new Color(0.96f, 0.86f, 0.64f));
            label.rectTransform.sizeDelta = new Vector2(228f, 82f);
            label.fontStyle = FontStyle.Bold;
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.88f);
            shadow.effectDistance = new Vector2(2f, -2f);
            panel.transform.localScale = Vector3.one * 1.18f;
            for (var time = 0f; time < 1.7f; time += Time.unscaledDeltaTime)
            {
                if (panel == null || !panel) yield break;
                var progress = Mathf.Clamp01(time / 1.7f);
                panel.transform.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, progress * 3f)));
                group.alpha = 1f;
                yield return null;
            }
        }

        private IEnumerator FlyCardRoutine(Card card, PlayerSeat from, PlayerSeat to, bool returning)
        {
            yield return FlyCardArcRoutine(card, SeatEffectPosition(from), SeatEffectPosition(to), returning ? "还贡" : "进贡", 0f);
        }

        private IEnumerator FlyCardArcRoutine(Card card, Vector2 start, Vector2 end, string title, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var flying = CreateMiniCardGraphic(card, effectRoot, start, new Vector2(92f, 132f));
            var banner = CreateText("Exchange", title, flying, new Vector2(0f, -82f), 17, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.45f));
            banner.rectTransform.sizeDelta = new Vector2(120f, 34f);
            for (var time = 0f; time < 0.72f; time += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(time / 0.72f);
                var position = Vector2.Lerp(start, end, Mathf.SmoothStep(0f, 1f, progress));
                position.y += Mathf.Sin(progress * Mathf.PI) * 120f;
                flying.anchoredPosition = position;
                flying.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-12f, 10f, progress));
                yield return null;
            }
            Destroy(flying.gameObject);
        }

        private IEnumerator TreasureMoveRoutine(int team, int from, int to, bool chestOpened)
        {
            var color = team == 0 ? new Color(0.12f, 0.50f, 0.44f, 0.96f) : new Color(0.64f, 0.16f, 0.10f, 0.96f);
            var panel = CreatePanel("TreasureMove", effectRoot, new Vector2(0f, 40f), new Vector2(620f, 170f), color);
            panel.GetComponent<Image>().raycastTarget = false;
            var group = panel.AddComponent<CanvasGroup>();
            var message = chestOpened
                ? $"{(team == 0 ? "青队" : "朱队")}抵达第 {TreasureRace.TrackLength} 格\n宝箱开启"
                : $"{(team == 0 ? "青队" : "朱队")}推进\n第 {from} 格  →  第 {to} 格";
            var label = CreateText("Label", message, panel.transform, Vector2.zero, chestOpened ? 38 : 28, TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.50f));
            label.rectTransform.sizeDelta = new Vector2(590f, 150f);
            for (var time = 0f; time < 2.0f; time += Time.unscaledDeltaTime)
            {
                var progress = Mathf.Clamp01(time / 2f);
                panel.transform.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, Mathf.Min(1f, progress * 4f));
                group.alpha = progress < 0.72f ? 1f : 1f - (progress - 0.72f) / 0.28f;
                yield return null;
            }
            Destroy(panel);
        }

        private static Vector2 SeatEffectPosition(PlayerSeat seat) => seat switch
        {
            PlayerSeat.South => new Vector2(0f, -120f),
            PlayerSeat.East => new Vector2(650f, 45f),
            PlayerSeat.North => new Vector2(0f, 190f),
            PlayerSeat.West => new Vector2(-650f, 45f),
            _ => Vector2.zero,
        };

        private static Color SuitColor(Card card) => card.Suit is CardSuit.Diamonds or CardSuit.Hearts
            ? new Color(0.63f, 0.10f, 0.08f)
            : new Color(0.10f, 0.12f, 0.11f);

        private void RenderProfile()
        {
            if (!profilePanel.activeSelf) return;
            var profile = director.GetProfile(profileSeat);
            profileRoleText.text = $"{SeatName(profileSeat)} · 人物资料";
            profileNameText.text = profile.Name;
            profileTitleText.text = profile.Title;
            profileBodyText.text = profile.Description;
            profileTraitsText.text = $"性格：{string.Join("  ·  ", profile.Traits ?? Array.Empty<string>())}";
            profileReactionText.text = profile.Reaction;
        }

        private void ShowProfile(PlayerSeat seat)
        {
            profileSeat = seat;
            profilePanel.SetActive(true);
            RenderProfile();
        }

        private Button CreateCardButton(Card card, Transform parent, Vector2 position)
        {
            var button = CreateButton($"Card_{card.Id}", position, new Vector2(90f, 126f), CardLabel(card, director.Match.LevelRank), new Color(0.92f, 0.87f, 0.72f), UiHitKind.Card, -1, card.Id);
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = cardFaceSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.color = Color.white;
            }
            var cardLabel = button.GetComponentInChildren<Text>();
            if (cardLabel != null)
            {
                cardLabel.fontSize = 21;
                cardLabel.fontStyle = FontStyle.Bold;
                cardLabel.resizeTextMinSize = 14;
                cardLabel.resizeTextMaxSize = 21;
            }
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.91f, 0.68f, 1f);
            colors.pressedColor = new Color(0.92f, 0.66f, 0.28f, 1f);
            colors.disabledColor = new Color(0.72f, 0.69f, 0.59f, 1f);
            button.colors = colors;
            return button;
        }

        private Button CreateButton(string name, Vector2 position, Vector2 size, string text, Color color, UiHitKind kind, int index = -1, string value = null)
        {
            var go = new GameObject(name);
            Transform parent = root;
            if (name.StartsWith("Lot_")) parent = lotteryPanel.transform;
            else if (name == "Sound") parent = root;
            else if (name is "Steady" or "Risky" or "EnterTreasure" or "Continue" or "Restart") parent = routePanel.transform;
            else if (name == "ProfileClose") parent = profilePanel.transform;
            else if (name == "VariantClose") parent = variantPanel.transform;
            else if (name.StartsWith("Variant_")) parent = variantRoot;
            else if (name.StartsWith("Card_")) parent = handRoot;
            else if (name.StartsWith("Profile") || name is "Play" or "Pass") parent = tablePanel.transform;
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            var authoredFrame = name is "ProfileNorth" or "ProfileEast" or "ProfileWest" or "Play" or "Pass";
            image.color = authoredFrame ? Color.white : color;
            if (name.StartsWith("Lot_"))
                ApplySkin(image, tombPanelSprite, Color.white);
            else if (authoredFrame)
                ApplyAspectSkin(image, seatPlaqueSprite, Color.white);
            else if (!name.StartsWith("Card_"))
                ApplySkin(image, tombButtonSprite, color);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = authoredFrame ? Color.white : color,
                highlightedColor = authoredFrame ? new Color(1f, 0.93f, 0.76f, 1f) : Color.Lerp(color, Color.white, 0.16f),
                pressedColor = authoredFrame ? new Color(0.92f, 0.78f, 0.56f, 1f) : Color.Lerp(color, Color.black, 0.16f),
                selectedColor = authoredFrame ? Color.white : color,
                disabledColor = authoredFrame ? new Color(0.65f, 0.65f, 0.60f, 0.72f) : new Color(color.r, color.g, color.b, 0.35f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            var label = CreateText("Label", text, rect, Vector2.zero, 20, TextAnchor.MiddleCenter, Color.white);
            label.rectTransform.sizeDelta = size - new Vector2(14f, 10f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 20;
            if (authoredFrame)
            {
                label.fontStyle = FontStyle.Bold;
                var shadow = label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.88f);
                shadow.effectDistance = new Vector2(2f, -2f);
                var accent = CreatePanelStatic("Accent", rect, new Vector2(0f, -size.y * 0.34f), new Vector2(size.x * 0.42f, 3f), color);
                accent.GetComponent<Image>().raycastTarget = false;
            }
            var target = go.AddComponent<UiHitTarget>();
            target.Configure(this, kind, index, value);
            var collider = go.GetComponent<BoxCollider>();
            if (collider != null) collider.size = new Vector3(size.x, size.y, 22f);
            target.BindButton(button);
            return button;
        }

        private static void StyleLotteryCard(Button button, int index)
        {
            if (button == null) return;
            var rect = button.transform as RectTransform;
            if (rect == null) return;
            var image = button.targetGraphic as Image;
            if (image != null)
            {
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                image.raycastTarget = true;
            }

            // A separate ink field gives the square tomb panel a deliberate vertical
            // card silhouette. It is inserted behind the text and never scales the
            // artwork itself, so names remain crisp and fully inside the frame.
            var ink = new GameObject("InkField", typeof(RectTransform), typeof(Image));
            ink.transform.SetParent(rect, false);
            ink.transform.SetAsFirstSibling();
            var inkRect = ink.transform as RectTransform;
            inkRect.sizeDelta = new Vector2(rect.sizeDelta.x - 34f, rect.sizeDelta.y - 40f);
            inkRect.anchoredPosition = new Vector2(0f, -2f);
            var inkImage = ink.GetComponent<Image>();
            inkImage.color = new Color(0.025f, 0.034f, 0.032f, 0.94f);
            inkImage.raycastTarget = false;

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 22;
                label.color = new Color(0.94f, 0.80f, 0.46f, 1f);
                label.fontStyle = FontStyle.Bold;
                label.lineSpacing = 1.12f;
                label.rectTransform.sizeDelta = rect.sizeDelta - new Vector2(44f, 68f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -5f);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 22;
            }

            var seal = new GameObject($"Seal_{index + 1}", typeof(RectTransform), typeof(Text));
            seal.transform.SetParent(rect, false);
            var sealRect = seal.transform as RectTransform;
            sealRect.sizeDelta = new Vector2(120f, 26f);
            sealRect.anchoredPosition = new Vector2(0f, rect.sizeDelta.y * 0.5f - 31f);
            var sealText = seal.GetComponent<Text>();
            sealText.font = ResolveFont();
            sealText.text = index switch { 0 => "身份签  ·  甲", 1 => "身份签  ·  乙", _ => "身份签  ·  丙" };
            sealText.fontSize = 13;
            sealText.alignment = TextAnchor.MiddleCenter;
            sealText.color = new Color(0.74f, 0.84f, 0.76f, 1f);
            sealText.raycastTarget = false;
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.color = color;
            return go;
        }

        private RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private Text CreateText(string name, string text, Transform parent, Vector2 position, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000f, 80f);
            rect.anchoredPosition = position;
            var label = go.AddComponent<Text>();
            label.font = ResolveFont();
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(11, size - 8);
            label.resizeTextMaxSize = size;
            return label;
        }

        private static void ApplySkin(Image image, Sprite sprite, Color color)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
        }

        private static void ApplyAspectSkin(Image image, Sprite sprite, Color color)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = color;
        }

        private static Sprite LoadSpriteResource(string resourcePath)
        {
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null) return sprite;
            var texture = Resources.Load<Texture2D>(resourcePath);
            return texture == null
                ? null
                : Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Font ResolveFont()
        {
            if (uiFont != null) return uiFont;
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Noto Sans CJK SC", "PingFang SC", "Microsoft YaHei", "Arial" },
                24);
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return uiFont;
        }

        private void SetButtonText(string name, string text)
        {
            var button = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
            var label = button != null ? button.GetComponentInChildren<Text>() : null;
            if (label != null) label.text = text;
        }

        private string SeatCount(PlayerSeat seat)
        {
            var match = director.Match;
            if (match == null) return "";
            for (var index = 0; index < match.FinishOrder.Count; index++)
            {
                if (match.FinishOrder[index] == seat) return $"第 {index + 1} 名";
            }
            if (director.IsPresentationLocked) return "收牌中";
            var count = match.GetHand(seat).Count;
            return count > 10 ? "?张" : $"{count}张";
        }

        private static string SeatName(PlayerSeat seat) => seat switch
        {
            PlayerSeat.South => "你",
            PlayerSeat.East => "东家",
            PlayerSeat.North => "队友",
            PlayerSeat.West => "西家",
            _ => seat.ToString(),
        };

        private static string CardLabel(Card card, int levelRank)
        {
            var suit = card.Suit switch
            {
                CardSuit.Clubs => "♣",
                CardSuit.Diamonds => "♦",
                CardSuit.Hearts => "♥",
                CardSuit.Spades => "♠",
                _ => "王",
            };
            var wild = card.IsWildcard(levelRank) ? "\n配" : "";
            return $"{card.RankLabel}\n{suit}{wild}";
        }
    }
}
