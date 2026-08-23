using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Guandan.Scene;
using Guandan.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Guandan.Game
{
    public sealed class GameDirector : MonoBehaviour
    {
        // This title's VR design is intentionally hybrid: tomb, table and characters are 3D,
        // while every playable card and command is a flat panel in front of the player's eyes.
        // Keep this authoritative instead of relying on a mutable Scene toggle.
        private const bool UseFlatCardUiPresentation = true;

        [Header("Rule match")]
        [SerializeField] private bool useFixedDebugSeed;
        [SerializeField] private int debugSeed = 20260817;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool openingLottery = true;
        [Tooltip("在地宫 VR 场景中，把手牌、操作按钮和资料卡作为相机前的平面 UI 呈现；不生成桌面实体牌模型。")]
        [SerializeField] private bool useScreenUi = true;
        [Tooltip("关闭后不生成默认人物和 f4 箱子，保留座位锚点供你在 Scene 中自行摆放。")]
        [SerializeField] private bool spawnRuntimeAvatars;

        [Header("Editable scene anchors")]
        [SerializeField] private Transform playerHandAnchor;
        [SerializeField] private Transform tablePlayAnchor;
        [SerializeField] private Transform treasureTrackAnchor;
        [SerializeField] private Transform uiAnchor;
        [SerializeField] private Transform[] seatAnchors = new Transform[4];

        [Header("Runtime roots")]
        [SerializeField] private Transform cardRoot;
        [SerializeField] private Transform tablePlayRoot;
        [SerializeField] private Transform worldUiRoot;

        private readonly List<CardView> handViews = new();
        private readonly List<CardView> tableViews = new();
        private GuandanMatchEngine match;
        private TreasureRace race;
        private Coroutine aiRoutine;
        private Coroutine toastRoutine;
        private Coroutine dealRoutine;
        private Coroutine aiRouteRoutine;
        private Coroutine raceOpenRoutine;
        private Coroutine lotteryRoutine;
        private readonly HashSet<string> selectedIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> hintedIds = new(StringComparer.Ordinal);
        private readonly HashSet<PlayerSeat> placementStamps = new();
        private readonly Dictionary<PlayerSeat, TextMesh> seatLabels = new();
        private readonly Dictionary<PlayerSeat, string> seatPlayLabels = new();
        private readonly Dictionary<PlayerSeat, PlayPattern> seatPlayPatterns = new();
        private IReadOnlyList<PlayPattern> pendingVariants = Array.Empty<PlayPattern>();
        private TextMesh phaseLabel;
        private TextMesh messageLabel;
        private TextMesh raceLabel;
        private WorldButton playButton;
        private WorldButton passButton;
        private WorldButton hintButton;
        private WorldButton enterTreasureButton;
        private WorldButton steadyButton;
        private WorldButton riskyButton;
        private WorldButton continueButton;
        private WorldLotteryLot[] lotteryLots;
        private ScreenGameUi screenUi;
        private readonly Dictionary<PlayerSeat, WorldSeatStatusUi> worldSeatStatusUi = new();
        private GameAudio gameAudio;
        private TreasureScenePresenter treasurePresenter;
        private RuntimeGameplayBindings gameplayBindings;
        private PlayerProfile[] lotteryProfiles;
        private bool lotteryChosen;
        private bool lotteryBusy;
        private bool presentationLocked;
        private bool raceOpen;
        private bool raceResolved;
        private bool muted;
        private int dealVisibleCount;
        private string presentationMessage = "等待抽签";
        private string lotteryStatusMessage = "选择一支竹签";
        private string routeResultMessage = string.Empty;
        private string message = string.Empty;
        private readonly string[] lotteryRevealLabels = new string[3];
        private int tributeAssignmentVisualIndex;
        private bool aiPromptRequested;
        private PlayerSeat thinkingSeat = PlayerSeat.South;
        private bool treasureViewActive;
        private int seed;
        private System.Random sessionRandom;
        private System.Random aiRandom;
        private System.Random raceRandom;
        private bool loggedKeyboardFallback;
        private KeyCode lastFallbackKey = KeyCode.None;
        private float lastFallbackKeyTime = -10f;

        public bool LotteryChosen => lotteryChosen;
        public bool LotteryBusy => lotteryBusy;
        public bool IsPresentationLocked => presentationLocked;
        public bool RaceOpen => raceOpen;
        public bool SpawnRuntimeAvatars => spawnRuntimeAvatars;
        public bool RaceResolved => raceResolved;
        public int DealVisibleCount => dealVisibleCount;
        public int SelectedCount => selectedIds.Count;
        public string PresentationMessage => presentationMessage;
        public string LotteryStatusMessage => lotteryStatusMessage;
        public string Message => message;
        public string RouteResultMessage => routeResultMessage;
        public bool Muted => muted;
        public bool TreasureViewActive => treasureViewActive;
        public bool CanSubmitSelection => CanSubmitSelectedCards();

        public GuandanMatchEngine Match => match;
        public TreasureRace Race => race;

        public Transform GetAvatarTransform(PlayerSeat seat)
        {
            return gameplayBindings != null ? gameplayBindings.GetAvatarTransform(seat) : null;
        }

        public string GetLotteryLotText(int index)
        {
            if (index < 0 || index >= lotteryRevealLabels.Length) return string.Empty;
            return string.IsNullOrEmpty(lotteryRevealLabels[index])
                ? $"{new[] { "甲", "乙", "丙" }[index]}签\n点击抽取"
                : lotteryRevealLabels[index];
        }

        public PlayerProfile GetProfile(PlayerSeat seat)
        {
            if (seat == PlayerSeat.South) return new PlayerProfile("你", "真人发掘领队", "与你的队友一起完成牌局并打开中央宝箱。", "稳住牌权，先看清桌面。", new[] { "真人", "发掘领队" }, 0f, 0f, 0f, 0f);
            if (lotteryProfiles == null || lotteryProfiles.Length < 3) lotteryProfiles = CreateLotteryProfiles(NextSeed());
            return seat switch
            {
                PlayerSeat.North => lotteryProfiles[0],
                PlayerSeat.East => lotteryProfiles[1],
                PlayerSeat.West => lotteryProfiles[2],
                _ => lotteryProfiles[0],
            };
        }

        public string GetSeatPlayLabel(PlayerSeat seat)
        {
            return seatPlayLabels.TryGetValue(seat, out var value) ? value : string.Empty;
        }

        public IReadOnlyList<Card> GetSeatPlayCards(PlayerSeat seat)
        {
            return seatPlayPatterns.TryGetValue(seat, out var play) && play != null
                ? OrderPatternCards(play)
                : Array.Empty<Card>();
        }

        public bool IsCardSelected(string cardId) => !string.IsNullOrEmpty(cardId) && selectedIds.Contains(cardId);
        public bool IsHinted(string cardId) => !string.IsNullOrEmpty(cardId) && hintedIds.Contains(cardId);

        public bool IsCardSelectable(string cardId)
        {
            if (match == null || presentationLocked || match.ActiveSeat != PlayerSeat.South) return false;
            if (match.Phase == MatchPhase.TributeReturn)
                return match.GetLegalReturnCards(PlayerSeat.South).Any(card => card.Id == cardId);
            return match.Phase == MatchPhase.Playing;
        }

        public void ToggleCardById(string cardId)
        {
            if (match == null || !IsCardSelectable(cardId)) return;
            var view = handViews.FirstOrDefault(item => item.Card?.Id == cardId);
            if (view != null) ToggleCard(view);
            else
            {
                if (match.Phase == MatchPhase.TributeReturn)
                {
                    selectedIds.Clear();
                    selectedIds.Add(cardId);
                    hintedIds.Clear();
                    Refresh();
                    return;
                }
                if (!selectedIds.Add(cardId)) selectedIds.Remove(cardId);
                hintedIds.Clear();
                Refresh();
            }
        }

        public void TryPlaySelected() => SubmitSelectedCards();

        public void ChooseVariant(int index)
        {
            if (match == null || index < 0 || index >= pendingVariants.Count) return;
            var candidate = pendingVariants[index];
            pendingVariants = Array.Empty<PlayPattern>();
            match.Play(PlayerSeat.South, candidate);
            selectedIds.Clear();
            hintedIds.Clear();
            screenUi?.HideVariantChoices();
            Refresh();
        }

        public void ToggleSound()
        {
            muted = !muted;
            if (gameAudio != null) gameAudio.Muted = muted;
            ShowMessage(muted ? "声音已关闭" : "声音已打开");
            screenUi?.Refresh();
        }

        public void ChooseLottery(int selectedIndex)
        {
            if (lotteryChosen || lotteryBusy || selectedIndex < 0 || selectedIndex >= 3) return;
            lotteryRoutine = StartCoroutine(LotteryPresentation(selectedIndex));
        }

        private IEnumerator LotteryPresentation(int selectedIndex)
        {
            lotteryBusy = true;
            presentationLocked = true;
            lotteryStatusMessage = "竹签正在逐支显字…";
            Array.Clear(lotteryRevealLabels, 0, lotteryRevealLabels.Length);
            var drawn = lotteryProfiles.ToArray();
            var chosen = drawn[selectedIndex];
            var opponents = drawn.Where((_, index) => index != selectedIndex).ToArray();
            lotteryProfiles = new[] { chosen, opponents[0], opponents[1] };
            RefreshWorldLottery();
            screenUi?.Refresh();

            var revealOrder = new[] { selectedIndex }
                .Concat(Enumerable.Range(0, drawn.Length).Where(index => index != selectedIndex));
            foreach (var slot in revealOrder)
            {
                var role = slot == selectedIndex
                    ? "北家 · 队友"
                    : slot == Array.IndexOf(drawn, opponents[0]) ? "东家 · 对手" : "西家 · 对手";
                lotteryRevealLabels[slot] = $"{new[] { "甲", "乙", "丙" }[slot]}签\n{drawn[slot].Name}\n{role}";
                gameAudio?.Card();
                RefreshWorldLottery();
                screenUi?.Refresh();
                yield return new WaitForSeconds(0.48f);
            }

            lotteryStatusMessage = $"你与 {chosen.Name} 同队，对阵 {opponents[0].Name}、{opponents[1].Name}";
            RefreshWorldLottery();
            screenUi?.Refresh();
            yield return new WaitForSeconds(1.15f);
            lotteryChosen = true;
            lotteryBusy = false;
            lotteryRoutine = null;
            ShowMessage($"{chosen.Name}成为你的队友，其余两人加入对手队");
            StartMatchWithPresentation();
        }

        private void StartMatchWithPresentation()
        {
            raceOpen = false;
            raceResolved = false;
            routeResultMessage = string.Empty;
            selectedIds.Clear();
            hintedIds.Clear();
            seatPlayLabels.Clear();
            seatPlayPatterns.Clear();
            placementStamps.Clear();
            screenUi?.ClearPlacementStamps();
            tributeAssignmentVisualIndex = 0;
            match.StartMatch();
            StartDealPresentation();
        }

        private void StartDealPresentation()
        {
            if (dealRoutine != null) StopCoroutine(dealRoutine);
            dealRoutine = StartCoroutine(DealPresentation());
        }

        private IEnumerator DealPresentation()
        {
            presentationLocked = true;
            dealVisibleCount = 0;
            presentationMessage = "四家循环发牌 · 0 / 27";
            Refresh();
            for (var count = 1; count <= 27; count++)
            {
                dealVisibleCount = count;
                presentationMessage = $"四家循环发牌 · {count} / 27";
                if ((count - 1) % 4 == 0) gameAudio?.Deal();
                Refresh();
                yield return new WaitForSeconds(0.055f);
            }
            var level = GuandanMatchEngine.RankLabel(match.LevelRank);
            presentationMessage = $"第 {match.HandNumber} 小局 · 打 {level}\n红桃 {level} 为逢人配";
            Refresh();
            gameAudio?.Reward();
            yield return new WaitForSeconds(1.65f);
            presentationMessage = string.Empty;
            presentationLocked = false;
            dealRoutine = null;
            match.BeginAfterDealPresentation();
            Refresh();
        }

        /// <summary>Editor setup entry point: serializes the editable anchors into the Scene.</summary>
        public void BuildSceneAnchorsForEditor()
        {
            EnsureRoots();
            if (!UseFlatCardUiPresentation)
            {
                BuildWorldUi();
                BuildSeatLabels();
            }
        }

        private void Awake()
        {
            ResetRandomStreams();
            EnsureRoots();
            if (UseFlatCardUiPresentation)
            {
                EnsureScreenUi();
                DisableLegacyWorldUi();
            }
            else
            {
                BuildWorldUi();
                BuildSeatLabels();
            }
            gameAudio = GetComponent<GameAudio>() ?? gameObject.AddComponent<GameAudio>();
            race = new TreasureRace();
            match = new GuandanMatchEngine(NextSeed());
            match.EventRaised += OnMatchEvent;
            match.StateChanged += Refresh;
            lotteryProfiles = CreateLotteryProfiles(NextSeed());
            try
            {
                treasurePresenter = GetComponent<TreasureScenePresenter>() ?? gameObject.AddComponent<TreasureScenePresenter>();
                treasurePresenter.Initialize(transform.root);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Guandan] 夺宝场景表现初始化失败，牌局仍可继续：{exception.Message}");
            }

            try
            {
                gameplayBindings = GetComponent<RuntimeGameplayBindings>() ?? gameObject.AddComponent<RuntimeGameplayBindings>();
                gameplayBindings.Initialize(this, seatAnchors, NextSeed(), spawnRuntimeAvatars);
                BuildSpatialSeatStatusUi();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Guandan] 人物与场景交互初始化失败，牌局仍可继续：{exception.Message}");
            }

            if (autoStart)
            {
                lotteryChosen = !openingLottery;
                if (lotteryChosen) StartMatchWithPresentation();
                else
                {
                    presentationLocked = true;
                    presentationMessage = "选择一支竹签开始发牌";
                    Refresh();
                }
            }
        }

        private void Start()
        {
            // Runtime bindings are allowed to attach scene helpers during Awake.  Reapply the
            // presentation boundary once all scene components have initialized so the restored
            // flat-card mode can never reactivate the retired tabletop CardView hierarchy.
            if (UseFlatCardUiPresentation) DisableLegacyWorldUi();
        }

        private void Update()
        {
            if (!lotteryChosen)
            {
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.digit1Key.wasPressedThisFrame) HandleFallbackKey(KeyCode.Alpha1);
                    else if (Keyboard.current.digit2Key.wasPressedThisFrame) HandleFallbackKey(KeyCode.Alpha2);
                    else if (Keyboard.current.digit3Key.wasPressedThisFrame) HandleFallbackKey(KeyCode.Alpha3);
                }
                return;
            }
            if (match == null || presentationLocked) return;
            HandleKeyboardCardFallback();
#if UNITY_EDITOR
            if (Keyboard.current != null)
            {
                if (Keyboard.current.f8Key.wasPressedThisFrame) DebugPlayWeakest();
                else if (Keyboard.current.bKey.wasPressedThisFrame) screenUi?.PlayBombImpact("同花顺 · 演出预览");
                else if (Keyboard.current.tKey.wasPressedThisFrame)
                {
                    var card = match.GetHand(PlayerSeat.South).FirstOrDefault();
                    screenUi?.PlayFlyCard(card, PlayerSeat.South, PlayerSeat.North, false);
                }
                else if (Keyboard.current.mKey.wasPressedThisFrame) screenUi?.PlayPlacementStamp(PlayerSeat.South, 1);
                else if (Keyboard.current.gKey.wasPressedThisFrame)
                {
                    screenUi?.PlayTreasureMove(0, 2, 5, false);
                    treasurePresenter?.MoveTeam(0, 2, 5, false);
                }
            }
#endif
            if (raceOpen && !raceResolved) return;
            if (match.Phase == MatchPhase.TributePayment && aiRoutine == null)
            {
                aiRoutine = StartCoroutine(RunTributePayment(match.ActiveSeat));
                return;
            }
            if (match.Phase == MatchPhase.TributeAssignment && aiRoutine == null)
            {
                aiRoutine = StartCoroutine(RunTributeAssignment());
                return;
            }
            if (match.Phase == MatchPhase.TributeReturn && match.ActiveSeat != PlayerSeat.South && aiRoutine == null)
            {
                aiRoutine = StartCoroutine(RunAiTributeReturn(match.ActiveSeat));
                return;
            }
            if (match.Phase == MatchPhase.Playing && match.ActiveSeat != PlayerSeat.South && aiRoutine == null)
            {
                aiRoutine = StartCoroutine(RunAiTurn(match.ActiveSeat));
            }
        }

        // Unity's Android View still receives key events when the PICO XR activity has
        // no input channel. IMGUI exposes those events even when the Input System keyboard
        // device is absent, so keep the same simulator fallback available through OnGUI.
        private void OnGUI()
        {
            var current = Event.current;
            if (current == null || current.type != EventType.KeyDown) return;
            HandleFallbackKey(current.keyCode);
        }

        private void HandleFallbackKey(KeyCode key)
        {
            if (key == KeyCode.None || Time.unscaledTime - lastFallbackKeyTime < 0.12f && lastFallbackKey == key)
                return;
            lastFallbackKey = key;
            lastFallbackKeyTime = Time.unscaledTime;

            if (!lotteryChosen)
            {
                if (key is KeyCode.Alpha1 or KeyCode.Keypad1) ChooseLottery(0);
                else if (key is KeyCode.Alpha2 or KeyCode.Keypad2) ChooseLottery(1);
                else if (key is KeyCode.Alpha3 or KeyCode.Keypad3) ChooseLottery(2);
                return;
            }

            if (key is KeyCode.Return or KeyCode.KeypadEnter)
            {
                HandleAction(GameAction.Play);
                return;
            }
            if (key == KeyCode.Space)
            {
                HandleAction(GameAction.Pass);
                return;
            }
            if (match == null || presentationLocked || match.ActiveSeat != PlayerSeat.South) return;
            var index = key switch
            {
                KeyCode.Alpha1 => 0,
                KeyCode.Alpha2 => 1,
                KeyCode.Alpha3 => 2,
                KeyCode.Alpha4 => 3,
                KeyCode.Alpha5 => 4,
                KeyCode.Alpha6 => 5,
                KeyCode.Alpha7 => 6,
                KeyCode.Alpha8 => 7,
                KeyCode.Alpha9 => 8,
                KeyCode.Alpha0 => 9,
                KeyCode.Keypad1 => 10,
                KeyCode.Keypad2 => 11,
                KeyCode.Keypad3 => 12,
                KeyCode.Keypad4 => 13,
                KeyCode.Keypad5 => 14,
                KeyCode.Keypad6 => 15,
                KeyCode.Keypad7 => 16,
                KeyCode.Keypad8 => 17,
                KeyCode.Keypad9 => 18,
                KeyCode.Keypad0 => 19,
                KeyCode.F2 => 20,
                KeyCode.F3 => 21,
                KeyCode.F4 => 22,
                KeyCode.F5 => 23,
                KeyCode.F6 => 24,
                KeyCode.F7 => 25,
                KeyCode.F8 => 26,
                _ => -1,
            };
            var hand = match.GetHand(PlayerSeat.South);
            if (index >= 0 && index < hand.Count) ToggleCardById(hand[index].Id);
        }

        /// <summary>
        /// PICO Emulator 0.13 can lose its virtual_input service, which removes the
        /// hand/controller click event while Android key events still reach Unity. Keep a
        /// deterministic keyboard path for the flat-card UI so the match remains playable
        /// during emulator diagnosis. Top-row digits select cards 1-10, numpad digits select
        /// cards 11-20, and F2-F8 select cards 21-27. Enter/Space retain Play/Pass in the XR
        /// bootstrap and therefore do not get repurposed here.
        /// </summary>
        private void HandleKeyboardCardFallback()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || match == null || match.ActiveSeat != PlayerSeat.South) return;
            if (match.Phase != MatchPhase.Playing && match.Phase != MatchPhase.TributeReturn) return;

            if (!loggedKeyboardFallback)
            {
                loggedKeyboardFallback = true;
                Debug.Log("[Guandan] PICO 模拟器键盘回退已启用：数字/小键盘/F2-F8 可选择平面牌 UI。");
            }

            var key = KeyCode.None;
            if (keyboard.digit1Key.wasPressedThisFrame) key = KeyCode.Alpha1;
            else if (keyboard.digit2Key.wasPressedThisFrame) key = KeyCode.Alpha2;
            else if (keyboard.digit3Key.wasPressedThisFrame) key = KeyCode.Alpha3;
            else if (keyboard.digit4Key.wasPressedThisFrame) key = KeyCode.Alpha4;
            else if (keyboard.digit5Key.wasPressedThisFrame) key = KeyCode.Alpha5;
            else if (keyboard.digit6Key.wasPressedThisFrame) key = KeyCode.Alpha6;
            else if (keyboard.digit7Key.wasPressedThisFrame) key = KeyCode.Alpha7;
            else if (keyboard.digit8Key.wasPressedThisFrame) key = KeyCode.Alpha8;
            else if (keyboard.digit9Key.wasPressedThisFrame) key = KeyCode.Alpha9;
            else if (keyboard.digit0Key.wasPressedThisFrame) key = KeyCode.Alpha0;
            else if (keyboard.numpad1Key.wasPressedThisFrame) key = KeyCode.Keypad1;
            else if (keyboard.numpad2Key.wasPressedThisFrame) key = KeyCode.Keypad2;
            else if (keyboard.numpad3Key.wasPressedThisFrame) key = KeyCode.Keypad3;
            else if (keyboard.numpad4Key.wasPressedThisFrame) key = KeyCode.Keypad4;
            else if (keyboard.numpad5Key.wasPressedThisFrame) key = KeyCode.Keypad5;
            else if (keyboard.numpad6Key.wasPressedThisFrame) key = KeyCode.Keypad6;
            else if (keyboard.numpad7Key.wasPressedThisFrame) key = KeyCode.Keypad7;
            else if (keyboard.numpad8Key.wasPressedThisFrame) key = KeyCode.Keypad8;
            else if (keyboard.numpad9Key.wasPressedThisFrame) key = KeyCode.Keypad9;
            else if (keyboard.numpad0Key.wasPressedThisFrame) key = KeyCode.Keypad0;
            else if (keyboard.f2Key.wasPressedThisFrame) key = KeyCode.F2;
            else if (keyboard.f3Key.wasPressedThisFrame) key = KeyCode.F3;
            else if (keyboard.f4Key.wasPressedThisFrame) key = KeyCode.F4;
            else if (keyboard.f5Key.wasPressedThisFrame) key = KeyCode.F5;
            else if (keyboard.f6Key.wasPressedThisFrame) key = KeyCode.F6;
            else if (keyboard.f7Key.wasPressedThisFrame) key = KeyCode.F7;
            else if (keyboard.f8Key.wasPressedThisFrame) key = KeyCode.F8;
            HandleFallbackKey(key);
        }

        public void HandleAction(GameAction action)
        {
            switch (action)
            {
                case GameAction.Play:
                    SubmitSelectedCards();
                    break;
                case GameAction.Pass:
                    selectedIds.Clear();
                    hintedIds.Clear();
                    match?.Pass(PlayerSeat.South);
                    break;
                case GameAction.Hint:
                    HighlightHint();
                    break;
                case GameAction.EnterTreasure:
                    EnterTreasureView();
                    break;
                case GameAction.SteadyRoute:
                    ResolveRace(TreasureRoute.Steady);
                    break;
                case GameAction.RiskyRoute:
                    ResolveRace(TreasureRoute.Risky);
                    break;
                case GameAction.ContinueHand:
                    if (match?.Phase == MatchPhase.HandComplete && raceResolved)
                    {
                        ExitTreasureView();
                        raceOpen = false;
                        raceResolved = false;
                        routeResultMessage = string.Empty;
                        selectedIds.Clear();
                        hintedIds.Clear();
                        seatPlayLabels.Clear();
                        seatPlayPatterns.Clear();
                        placementStamps.Clear();
                        screenUi?.ClearPlacementStamps();
                        match.StartNextHand();
                        StartDealPresentation();
                    }
                    break;
                case GameAction.RestartMatch:
                    if (aiRoutine != null) StopCoroutine(aiRoutine);
                    if (dealRoutine != null) StopCoroutine(dealRoutine);
                    if (aiRouteRoutine != null) StopCoroutine(aiRouteRoutine);
                    if (raceOpenRoutine != null) StopCoroutine(raceOpenRoutine);
                    if (lotteryRoutine != null) StopCoroutine(lotteryRoutine);
                    aiRoutine = null;
                    dealRoutine = null;
                    aiRouteRoutine = null;
                    raceOpenRoutine = null;
                    lotteryRoutine = null;
                    race.Reset();
                    ExitTreasureView();
                    treasurePresenter?.ResetPresentation();
                    ResetRandomStreams();
                    match = new GuandanMatchEngine(NextSeed());
                    match.EventRaised += OnMatchEvent;
                    match.StateChanged += Refresh;
                    lotteryProfiles = CreateLotteryProfiles(NextSeed());
                    gameplayBindings?.RerollAvatars(NextSeed());
                    lotteryChosen = false;
                    lotteryBusy = false;
                    lotteryStatusMessage = "选择一支竹签";
                    Array.Clear(lotteryRevealLabels, 0, lotteryRevealLabels.Length);
                    presentationLocked = true;
                    raceOpen = false;
                    raceResolved = false;
                    presentationMessage = "选择一支竹签开始发牌";
                    selectedIds.Clear();
                    hintedIds.Clear();
                    seatPlayLabels.Clear();
                    seatPlayPatterns.Clear();
                    placementStamps.Clear();
                    screenUi?.ClearPlacementStamps();
                    Refresh();
                    break;
            }
        }

        public void ToggleCard(CardView view)
        {
            if (view == null || match == null || match.ActiveSeat != PlayerSeat.South) return;
            if (match.Phase == MatchPhase.TributeReturn)
            {
                if (!match.GetLegalReturnCards(PlayerSeat.South).Any(card => card.Id == view.Card.Id))
                {
                    ShowMessage("这张牌不符合还贡条件");
                    return;
                }
                selectedIds.Clear();
                selectedIds.Add(view.Card.Id);
                hintedIds.Clear();
                Refresh();
                return;
            }
            if (match.Phase != MatchPhase.Playing) return;
            if (view.Selected)
            {
                selectedIds.Remove(view.Card.Id);
                view.SetSelected(false);
            }
            else
            {
                selectedIds.Add(view.Card.Id);
                view.SetSelected(true);
            }
            hintedIds.Clear();
            UpdateButtonAvailability();
            screenUi?.Refresh();
        }

        public void UseSocialProp(SocialPropType type, PlayerSeat target)
        {
            if (!lotteryChosen || raceOpen || match == null || match.Phase == MatchPhase.MatchComplete) return;
            var profile = GetProfile(target);
            var text = type == SocialPropType.Flower
                ? $"鲜花送给{profile.Name}。{profile.Reaction}"
                : $"西红柿飞向{profile.Name}。{profile.Reaction}";
            if (type == SocialPropType.Flower) gameAudio?.Flower(); else gameAudio?.Tomato();
            gameplayBindings?.PlaySocialReaction(target, type);
            ShowMessage(text);
        }

        public void NotifySocialPropPickup(SocialPropType type)
        {
            gameAudio?.Pickup(type);
        }

        public void PlayUiClick()
        {
            gameAudio?.UiClick();
        }

        public void RemindCurrentAi()
        {
            if (match == null || aiRoutine == null || match.ActiveSeat == PlayerSeat.South)
            {
                gameAudio?.Bell();
                ShowMessage("钟声在墓室里回荡，等待下一位牌手。");
                return;
            }
            aiPromptRequested = true;
            gameAudio?.Bell();
            ShowMessage($"钟声提醒{GuandanMatchEngine.SeatLabel(thinkingSeat)}尽快出牌。");
        }

        public void PlayBellFeedback(Vector3 position)
        {
            gameplayBindings?.PlayBellPulse(position);
        }

        public void PlayTreasureStepFeedback()
        {
            gameAudio?.TreasureStep();
        }

        public void EnterTreasureView()
        {
            if (!raceOpen) return;
            treasureViewActive = true;
            FindFirstObjectByType<Guandan.XR.GuandanXRBootstrap>()?.SetGameplayView(true);
            screenUi?.Refresh();
        }

        public void ExitTreasureView()
        {
            if (!treasureViewActive) return;
            treasureViewActive = false;
            FindFirstObjectByType<Guandan.XR.GuandanXRBootstrap>()?.SetGameplayView(false);
        }

        public void RecenterView()
        {
            var camera = Camera.main;
            if (camera == null) return;
            FindFirstObjectByType<Guandan.XR.GuandanXRBootstrap>()?.RecenterToDesignStart();
            ShowMessage("视角已回到南家桌边起点");
        }

        private IEnumerator RunAiTurn(PlayerSeat seat)
        {
            var profile = GetProfile(seat);
            thinkingSeat = seat;
            aiPromptRequested = false;
            gameplayBindings?.SetThinking(seat, true);
            var legalCount = match.GetLegalPlays(seat).Count;
            var complexity = Mathf.Clamp01((legalCount - 3f) / 18f) * 0.85f
                + (match.CurrentPlay != null ? 0.32f : 0f)
                + (match.GetHand(seat).Count <= 8 ? 0.30f : 0f);
            var thinkingSeconds = Mathf.Lerp(profile.ThinkMin, profile.ThinkMax, (float)aiRandom.NextDouble()) + complexity;
            if (aiRandom.NextDouble() < 0.10 + (1f - profile.RiskBias) * 0.08f)
                thinkingSeconds += Mathf.Lerp(1.8f, 4.6f, (float)aiRandom.NextDouble());
            var thinkUntil = Time.time + Mathf.Clamp(thinkingSeconds, 0.45f, 8.5f);
            while (Time.time < thinkUntil)
            {
                if (aiPromptRequested && thinkingSeat == seat)
                {
                    aiPromptRequested = false;
                    thinkUntil = Mathf.Min(thinkUntil, Time.time + 0.62f);
                }
                yield return null;
            }
            gameplayBindings?.SetThinking(seat, false);
            aiRoutine = null;
            if (match == null || match.Phase != MatchPhase.Playing || match.ActiveSeat != seat) yield break;
            var personality = new AiPersonality(profile.Name, profile.RiskBias, profile.BombBias, profile.ThinkMin, profile.ThinkMax);
            var chosen = GuandanAI.ChoosePlay(match, seat, personality, aiRandom);
            if (chosen == null && match.CurrentPlay != null) match.Pass(seat);
            else if (chosen != null) match.Play(seat, chosen);
        }

#if UNITY_EDITOR
        private void DebugPlayWeakest()
        {
            if (match == null || match.Phase != MatchPhase.Playing || match.ActiveSeat != PlayerSeat.South) return;
            var candidate = match.GetLegalPlays(PlayerSeat.South).FirstOrDefault();
            if (candidate != null) match.Play(PlayerSeat.South, candidate);
            else if (match.CurrentPlay != null) match.Pass(PlayerSeat.South);
        }
#endif

        private IEnumerator RunTributePayment(PlayerSeat seat)
        {
            yield return new WaitForSeconds(0.70f);
            aiRoutine = null;
            if (match == null || match.Phase != MatchPhase.TributePayment || match.ActiveSeat != seat) yield break;
            var card = match.GetLegalTributeCards(seat).FirstOrDefault();
            if (card != null) match.PayTribute(seat, card);
        }

        private IEnumerator RunTributeAssignment()
        {
            yield return new WaitForSeconds(0.70f);
            aiRoutine = null;
            if (match == null || match.Phase != MatchPhase.TributeAssignment) yield break;
            match.AssignEqualTributes();
        }

        private IEnumerator RunAiTributeReturn(PlayerSeat seat)
        {
            yield return new WaitForSeconds(0.8f);
            aiRoutine = null;
            if (match == null || match.Phase != MatchPhase.TributeReturn || match.ActiveSeat != seat) yield break;
            var card = GuandanAI.ChooseReturnTribute(match.GetLegalReturnCards(seat), match.LevelRank);
            if (card != null) match.ReturnTribute(seat, card);
        }

        public void SubmitSelectedCards()
        {
            if (match == null || selectedIds.Count == 0) return;
            if (match.Phase == MatchPhase.TributeReturn && match.ActiveSeat == PlayerSeat.South)
            {
                var returnCard = match.GetLegalReturnCards(PlayerSeat.South)
                    .FirstOrDefault(card => selectedIds.Contains(card.Id));
                if (returnCard == null || selectedIds.Count != 1)
                {
                    ShowMessage("请选择一张合法手牌还贡");
                    return;
                }
                match.ReturnTribute(PlayerSeat.South, returnCard);
                selectedIds.Clear();
                hintedIds.Clear();
                screenUi?.Refresh();
                return;
            }
            if (match.Phase != MatchPhase.Playing || match.ActiveSeat != PlayerSeat.South) return;
            var selected = match.GetHand(PlayerSeat.South).Where(card => selectedIds.Contains(card.Id)).ToArray();
            var candidates = GuandanRuleEngine.FindPatternsForSelection(selected, match.LevelRank, match.CurrentPlay);
            if (candidates.Count == 0)
            {
                ShowMessage("这组牌不能压过桌面牌型");
                return;
            }
            if (candidates.Count > 1 && screenUi != null)
            {
                pendingVariants = candidates;
                screenUi.ShowVariantChoices(candidates);
                return;
            }
            match.Play(PlayerSeat.South, candidates[0]);
            selectedIds.Clear();
            hintedIds.Clear();
            screenUi?.Refresh();
        }

        private bool CanSubmitSelectedCards()
        {
            if (match == null || presentationLocked || match.ActiveSeat != PlayerSeat.South || selectedIds.Count == 0) return false;
            if (match.Phase == MatchPhase.TributeReturn)
                return selectedIds.Count == 1 && match.GetLegalReturnCards(PlayerSeat.South).Any(card => selectedIds.Contains(card.Id));
            if (match.Phase != MatchPhase.Playing) return false;
            var selected = match.GetHand(PlayerSeat.South).Where(card => selectedIds.Contains(card.Id)).ToArray();
            return selected.Length == selectedIds.Count
                && GuandanRuleEngine.FindPatternsForSelection(selected, match.LevelRank, match.CurrentPlay).Count > 0;
        }

        private void HighlightHint()
        {
            var candidate = MinimumBeatCandidate();
            UpdateAutomaticHint();
            foreach (var view in handViews) view.SetHighlighted(candidate != null && candidate.Cards.Any(card => card.Id == view.Card.Id));
            ShowMessage(candidate == null ? "当前没有可压牌" : $"朱砂提示：{candidate.Label}");
            screenUi?.Refresh();
        }

        private void UpdateAutomaticHint()
        {
            hintedIds.Clear();
            var candidate = MinimumBeatCandidate();
            if (candidate == null) return;
            foreach (var card in candidate.Cards) hintedIds.Add(card.Id);
        }

        private PlayPattern MinimumBeatCandidate()
        {
            if (match == null
                || presentationLocked
                || raceOpen
                || match.Phase != MatchPhase.Playing
                || match.ActiveSeat != PlayerSeat.South
                || match.CurrentPlay == null)
                return null;

            var legal = match.GetLegalPlays(PlayerSeat.South);
            var weakest = legal.FirstOrDefault();
            if (weakest == null || weakest.Kind != PlayKind.FullHouse) return weakest;
            var strength = GuandanRuleEngine.Strength(weakest, match.LevelRank);
            return legal
                .Where(play => play.Kind == PlayKind.FullHouse
                    && GuandanRuleEngine.Strength(play, match.LevelRank) == strength)
                .OrderBy(play => FullHousePairCost(play, match.LevelRank))
                .ThenBy(play => play.Wildcards.Count)
                .ThenBy(play => play.Identity, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static int FullHousePairCost(PlayPattern play, int levelRank)
        {
            var groups = play.Cards
                .GroupBy(card => EffectiveRank(card, play))
                .ToDictionary(group => group.Key, group => group.Count());
            var pairRank = groups.FirstOrDefault(pair => pair.Value == 2).Key;
            return pairRank == levelRank ? 50 : pairRank;
        }

        private void ResolveRace(TreasureRoute route)
        {
            if (match == null || match.Phase != MatchPhase.HandComplete || match.LastHandResult == null || !raceOpen || raceResolved) return;
            var team = match.LastHandResult.WinningTeam;
            var previousPosition = race.GetPosition(team);
            var result = race.Move(team, match.LastHandResult.BaseSteps, route, raceRandom.NextDouble());
            routeResultMessage = $"{result.Label} · {(team == 0 ? "青队" : "朱队")}到达第 {result.Position} 格";
            raceResolved = true;
            ShowMessage(routeResultMessage);
            RefreshRaceLabel();
            steadyButton?.SetAvailable(false);
            riskyButton?.SetAvailable(false);
            if (race.IsComplete)
            {
                gameAudio?.Chest();
                match.MarkMatchComplete();
                continueButton?.SetAvailable(false);
                routeResultMessage = $"{(race.WinningTeam == 0 ? "青队" : "朱队")}率先打开中央宝箱";
                ShowMessage(routeResultMessage);
            }
            else
            {
                gameAudio?.Reward();
                continueButton?.SetAvailable(true);
            }
            screenUi?.PlayTreasureMove(team, previousPosition, result.Position, race.IsComplete);
            treasurePresenter?.MoveTeam(team, previousPosition, result.Position, race.IsComplete);
        }

        private IEnumerator RunAiRoute()
        {
            yield return new WaitForSeconds(1.0f);
            aiRouteRoutine = null;
            if (match == null || match.Phase != MatchPhase.HandComplete || !raceOpen || raceResolved) yield break;
            EnterTreasureView();
            var result = match.LastHandResult;
            var profile = GetProfile(result.FirstSeat);
            var route = GuandanAI.ChooseRoute(
                new AiPersonality(profile.Name, profile.RiskBias, profile.BombBias, profile.ThinkMin, profile.ThinkMax),
                race,
                result.WinningTeam,
                result.BaseSteps,
                aiRandom);
            ResolveRace(route);
        }

        private void OnMatchEvent(MatchEvent gameEvent)
        {
            if (gameEvent == null) return;
            switch (gameEvent.Type)
            {
                case MatchEventType.HandStarted:
                    tributeAssignmentVisualIndex = 0;
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.Error:
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.CardsPlayed:
                    var cardNames = gameEvent.Play == null ? string.Empty : string.Join(" ", gameEvent.Play.Cards.Select(card => card.ShortLabel));
                    seatPlayLabels[gameEvent.Seat] = $"{GuandanMatchEngine.SeatLabel(gameEvent.Seat)} · {gameEvent.Play?.Label ?? "出牌"}\n{cardNames}";
                    seatPlayPatterns[gameEvent.Seat] = gameEvent.Play;
                    if (gameEvent.Play?.IsBomb == true)
                    {
                        gameAudio?.Bomb();
                        screenUi?.PlayBombImpact(gameEvent.Play.Label);
                    }
                    else gameAudio?.Card();
                    ShowMessage($"{gameEvent.Message}");
                    break;
                case MatchEventType.Passed:
                    seatPlayPatterns.Remove(gameEvent.Seat);
                    seatPlayLabels[gameEvent.Seat] = $"{GuandanMatchEngine.SeatLabel(gameEvent.Seat)}\n过牌";
                    gameAudio?.Pass();
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.TrickReset:
                    seatPlayLabels.Clear();
                    seatPlayPatterns.Clear();
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.TributePaid:
                    gameAudio?.Tribute();
                    screenUi?.PlayFlyCardToCenter(gameEvent.Card, gameEvent.Seat);
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.TributeAssigned:
                    screenUi?.PlayFlyCardFromCenter(
                        gameEvent.Card,
                        gameEvent.OtherSeat,
                        0.46f + tributeAssignmentVisualIndex++ * 0.18f);
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.TributeReturned:
                    gameAudio?.ReturnTribute();
                    screenUi?.PlayFlyCard(gameEvent.Card, gameEvent.Seat, gameEvent.OtherSeat, true);
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.AntiTribute:
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.PlayerFinished:
                    placementStamps.Add(gameEvent.Seat);
                    screenUi?.PlayPlacementStamp(gameEvent.Seat, match.FinishOrder.Count);
                    ShowMessage(gameEvent.Message);
                    break;
                case MatchEventType.HandFinished:
                    if (gameEvent.HandResult?.Placements != null)
                    {
                        for (var index = 0; index < gameEvent.HandResult.Placements.Count; index++)
                        {
                            var seat = gameEvent.HandResult.Placements[index];
                            if (placementStamps.Add(seat)) screenUi?.PlayPlacementStamp(seat, index + 1);
                        }
                    }
                    ShowMessage(gameEvent.Message);
                    break;
            }
            Refresh();
        }

        private void Refresh()
        {
            if (match == null) return;
            if (match.Phase == MatchPhase.HandComplete && !raceOpen && raceOpenRoutine == null)
            {
                raceOpenRoutine = StartCoroutine(OpenRaceAfterPlacements());
            }
            UpdateAutomaticHint();
            if (!UseFlatCardUiPresentation)
            {
                RenderHand();
                RenderTablePlay();
                RefreshSeatLabels();
                RefreshRaceLabel();
                RefreshWorldLottery();
                UpdateButtonAvailability();
            }
            screenUi?.Refresh();
        }

        private IEnumerator OpenRaceAfterPlacements()
        {
            yield return new WaitForSeconds(1.10f);
            raceOpenRoutine = null;
            if (match == null || match.Phase != MatchPhase.HandComplete) yield break;
            screenUi?.ClearPlacementStamps();
            raceOpen = true;
            raceResolved = false;
            routeResultMessage = string.Empty;
            screenUi?.Refresh();
            if (match.LastHandResult != null && match.LastHandResult.WinningTeam != 0 && aiRouteRoutine == null)
                aiRouteRoutine = StartCoroutine(RunAiRoute());
        }

        private void RenderHand()
        {
            if (UseFlatCardUiPresentation) return;
            foreach (var view in handViews) Destroy(view.gameObject);
            handViews.Clear();
            if (playerHandAnchor == null) return;
            var hand = match.GetHand(PlayerSeat.South);
            var firstRow = Mathf.Min(14, hand.Count);
            for (var index = 0; index < hand.Count; index++)
            {
                var row = index < firstRow ? 0 : 1;
                var column = row == 0 ? index : index - firstRow;
                var rowCount = row == 0 ? firstRow : hand.Count - firstRow;
                var canSelect = (match.Phase == MatchPhase.Playing || match.Phase == MatchPhase.TributeReturn) && match.ActiveSeat == PlayerSeat.South;
                var view = CreateCardView(hand[index], playerHandAnchor, canSelect);
                var x = (column - (rowCount - 1) * 0.5f) * 0.52f;
                view.transform.localPosition = new Vector3(x, row * 0.58f, 0f);
                view.transform.localRotation = Quaternion.Euler(0f, 0f, row == 0 ? 0f : 1.5f);
                view.CaptureBasePosition();
                view.SetSelected(selectedIds.Contains(hand[index].Id));
                handViews.Add(view);
            }
        }

        private void RenderTablePlay()
        {
            if (UseFlatCardUiPresentation) return;
            foreach (var view in tableViews) Destroy(view.gameObject);
            tableViews.Clear();
            if (tablePlayAnchor == null || match.CurrentPlay == null) return;
            var cards = match.CurrentPlay.Cards;
            for (var i = 0; i < cards.Count; i++)
            {
                var view = CreateCardView(cards[i], tablePlayAnchor, false);
                view.transform.localPosition = new Vector3((i - (cards.Count - 1) * 0.5f) * 0.35f, 0.1f + i * 0.012f, 0f);
                view.transform.localRotation = Quaternion.Euler(0f, 0f, (i - cards.Count * 0.5f) * 2.2f);
                tableViews.Add(view);
            }
        }

        private CardView CreateCardView(Card card, Transform parent, bool selectable)
        {
            var go = new GameObject($"Card_{card.Id}");
            go.name = $"Card_{card.Id}";
            go.transform.SetParent(parent, false);
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.46f, 0.06f, 0.70f);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "CardVisual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(0.46f, 0.06f, 0.70f);
            var cardTexture = Resources.Load<Texture2D>("GuandanUI/CardFace");
            var cardMaterial = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Standard"))
            {
                mainTexture = cardTexture,
                color = cardTexture != null ? Color.white : new Color(0.90f, 0.84f, 0.68f),
            };
            visual.GetComponent<Renderer>().material = cardMaterial;
            Destroy(visual.GetComponent<Collider>());
            var view = go.AddComponent<CardView>();
            var textGo = new GameObject("CardLabel");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            textGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var label = textGo.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            // 0.46m-wide physical cards need a compact, tabletop-scale inscription.
            label.characterSize = 0.024f;
            view.Configure(card, this, selectable);
            return view;
        }

        private void BuildSeatLabels()
        {
            if (seatLabels.Count > 0) return;
            var names = new[] { "你 · 南家", "东家", "队友 · 北家", "西家" };
            for (var i = 0; i < 4; i++)
            {
                var anchor = seatAnchors != null && seatAnchors.Length > i ? seatAnchors[i] : null;
                if (anchor == null) continue;
                var labelTransform = anchor.Find("SeatLabel");
                var go = labelTransform != null ? labelTransform.gameObject : new GameObject("SeatLabel");
                if (labelTransform == null)
                {
                    go.transform.SetParent(anchor, false);
                    go.transform.localPosition = Vector3.up * 2.55f;
                }
                var text = go.GetComponent<TextMesh>();
                if (text == null) text = go.AddComponent<TextMesh>();
                text.text = names[i];
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 0.026f;
                seatLabels[(PlayerSeat)i] = text;
                var avatarTarget = anchor.gameObject.GetComponent<AvatarTarget>();
                if (avatarTarget == null) avatarTarget = anchor.gameObject.AddComponent<AvatarTarget>();
                avatarTarget.Configure((PlayerSeat)i);
            }
        }

        private void BuildWorldUi()
        {
            if (uiAnchor == null) return;
            // These are physical tabletop markers, never camera-attached UI.  A player can
            // walk around the table and operate them from a controller ray or direct desktop ray.
            phaseLabel = EnsureText("Phase", "墓室牌局", uiAnchor, new Vector3(0f, 0.10f, 0f), 0.026f);
            messageLabel = EnsureText("Message", "准备发牌", uiAnchor, new Vector3(0f, 0.06f, -0.28f), 0.018f);
            raceLabel = EnsureText("Race", $"夺宝 · 青 0 / {TreasureRace.TrackLength} 朱 0 / {TreasureRace.TrackLength}", uiAnchor, new Vector3(0f, 0.025f, -0.54f), 0.016f);
            playButton = EnsureButton("PlayButton", "出牌", GameAction.Play, uiAnchor, new Vector3(-1.28f, 0f, -0.92f));
            passButton = EnsureButton("PassButton", "不出", GameAction.Pass, uiAnchor, new Vector3(-0.43f, 0f, -0.92f));
            hintButton = EnsureButton("HintButton", "提示", GameAction.Hint, uiAnchor, new Vector3(0.43f, 0f, -0.92f));
            continueButton = EnsureButton("ContinueButton", "继续牌局", GameAction.ContinueHand, uiAnchor, new Vector3(1.28f, 0f, -0.92f));
            // A real tomb-floor console transports the view to the authored race track.
            // Route choices sit beside that track; neither uses a camera canvas.
            enterTreasureButton = EnsureButton("EnterTreasure", "前往夺宝台", GameAction.EnterTreasure, treasureTrackAnchor, new Vector3(0f, 0.08f, 0f));
            steadyButton = EnsureButton("SteadyRoute", "稳当探方", GameAction.SteadyRoute, treasureTrackAnchor, new Vector3(-2.0f, 0.08f, 17.0f));
            riskyButton = EnsureButton("RiskyRoute", "激进探方", GameAction.RiskyRoute, treasureTrackAnchor, new Vector3(2.0f, 0.08f, 17.0f));
            BuildWorldLottery();
            steadyButton.SetAvailable(false);
            riskyButton.SetAvailable(false);
            enterTreasureButton.SetAvailable(false);
            continueButton.SetAvailable(false);
        }

        private WorldButton EnsureButton(string name, string text, GameAction action, Transform parent, Vector3 localPosition)
        {
            var existing = parent != null ? parent.Find(name) : null;
            if (existing == null) return CreateButton(name, text, action, parent, localPosition);
            existing.localPosition = localPosition;
            existing.localRotation = Quaternion.identity;
            var button = existing.GetComponent<WorldButton>();
            if (button == null) button = existing.gameObject.AddComponent<WorldButton>();
            button.Configure(this, action, text);
            return button;
        }

        private void BuildWorldLottery()
        {
            var existingRoot = worldUiRoot != null ? worldUiRoot.Find("LotteryTable_三支同行签") : null;
            var root = existingRoot != null ? existingRoot : CreateRoot("LotteryTable_三支同行签", worldUiRoot);
            root.localPosition = new Vector3(0f, 2.26f, -0.76f);
            root.localRotation = Quaternion.identity;
            lotteryLots = new WorldLotteryLot[3];
            for (var index = 0; index < lotteryLots.Length; index++)
            {
                var child = root.Find($"Lot_{index + 1}");
                var lot = child != null ? child.GetComponent<WorldLotteryLot>() : null;
                if (lot == null)
                {
                    var lotObject = new GameObject($"Lot_{index + 1}");
                    lotObject.transform.SetParent(root, false);
                    lot = lotObject.AddComponent<WorldLotteryLot>();
                }
                lot.transform.localPosition = new Vector3((index - 1) * 0.88f, 0.10f, 0f);
                lot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                lot.Configure(this, index);
                lotteryLots[index] = lot;
            }
        }

        private WorldButton CreateButton(string name, string text, GameAction action, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.82f, 0.08f, 0.38f);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "ButtonVisual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(0.82f, 0.08f, 0.38f);
            var visualCollider = visual.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(visualCollider); else DestroyImmediate(visualCollider);
            var button = go.AddComponent<WorldButton>();
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.055f, 0f);
            textGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var label = textGo.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.11f;
            button.Configure(this, action, text);
            return button;
        }

        private TextMesh CreateText(string name, string text, Transform parent, Vector3 localPosition, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = size;
            return label;
        }

        private TextMesh EnsureText(string name, string text, Transform parent, Vector3 localPosition, float size)
        {
            var existing = parent != null ? parent.Find(name) : null;
            if (existing == null) return CreateText(name, text, parent, localPosition, size);
            var label = existing.GetComponent<TextMesh>();
            if (label == null) label = existing.gameObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = size;
            return label;
        }

        private void EnsureRoots()
        {
            var directorRoot = transform;
            cardRoot = cardRoot != null ? cardRoot : CreateRoot("Cards", directorRoot);
            tablePlayRoot = tablePlayRoot != null ? tablePlayRoot : CreateRoot("TablePlays", directorRoot);
            worldUiRoot = worldUiRoot != null ? worldUiRoot : CreateRoot("WorldUI", directorRoot);
            if (playerHandAnchor == null) playerHandAnchor = CreateRoot("HandAnchor_South", directorRoot);
            if (tablePlayAnchor == null) tablePlayAnchor = CreateRoot("TablePlayAnchor", directorRoot);
            if (treasureTrackAnchor == null) treasureTrackAnchor = CreateRoot("TreasureTrackAnchor", directorRoot);
            if (uiAnchor == null) uiAnchor = CreateRoot("UIAnchor", directorRoot);
            // Player cards and played cards are laid on the actual round table, so all
            // core card interactions retain parallax and can be reached from either side.
            playerHandAnchor.localPosition = new Vector3(0f, 2.24f, -2.18f);
            tablePlayAnchor.localPosition = new Vector3(0f, 2.24f, 0f);
            treasureTrackAnchor.localPosition = new Vector3(0f, 0.72f, 8.7f);
            uiAnchor.localPosition = new Vector3(0f, 2.24f, -1.10f);
            if (seatAnchors == null || seatAnchors.Length != 4) seatAnchors = new Transform[4];
            var seatRoot = transform.root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "Seats_四家座位与角色锚点");
            if (seatRoot != null)
            {
                seatAnchors[0] = seatAnchors[0] != null ? seatAnchors[0] : seatRoot.Find("Seat_South_玩家/AvatarAnchor");
                seatAnchors[1] = seatAnchors[1] != null ? seatAnchors[1] : seatRoot.Find("Seat_East_对手/AvatarAnchor");
                seatAnchors[2] = seatAnchors[2] != null ? seatAnchors[2] : seatRoot.Find("Seat_North_队友/AvatarAnchor");
                seatAnchors[3] = seatAnchors[3] != null ? seatAnchors[3] : seatRoot.Find("Seat_West_对手/AvatarAnchor");
            }
        }

        private void RefreshWorldLottery()
        {
            if (lotteryLots == null || lotteryLots.Length != 3) return;
            for (var index = 0; index < lotteryLots.Length; index++)
            {
                var lot = lotteryLots[index];
                if (lot == null) continue;
                lot.SetPresentation(GetLotteryLotText(index), !lotteryChosen, !lotteryChosen && !lotteryBusy);
            }
        }

        private Transform CreateRoot(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private void RefreshSeatLabels()
        {
            foreach (var pair in seatLabels)
            {
                var seat = pair.Key;
                var text = pair.Value;
                var count = match.GetHand(seat).Count;
                var place = -1;
                for (var index = 0; index < match.FinishOrder.Count; index++)
                {
                    if (match.FinishOrder[index] == seat) { place = index; break; }
                }
                var suffix = place >= 0 ? $" · 第 {place + 1} 名" : count > 10 ? " · ?张" : $" · {count}张";
                text.text = GuandanMatchEngine.SeatLabel(seat) + suffix;
            }
        }

        private void RefreshRaceLabel()
        {
            if (raceLabel == null || race == null) return;
            raceLabel.text = $"夺宝 · 青 {race.GetPosition(0)} / {TreasureRace.TrackLength}    朱 {race.GetPosition(1)} / {TreasureRace.TrackLength}";
            treasurePresenter?.UpdateFlagScores(
                match != null ? match.GetTeamLevel(0) : 2,
                match != null ? match.GetTeamLevel(1) : 2,
                race.GetPosition(0), race.GetPosition(1));
        }

        private void UpdateButtonAvailability()
        {
            if (match == null) return;
            if (!lotteryChosen || presentationLocked)
            {
                playButton?.SetAvailable(false);
                passButton?.SetAvailable(false);
                hintButton?.SetAvailable(false);
                enterTreasureButton?.SetAvailable(false);
                steadyButton?.SetAvailable(false);
                riskyButton?.SetAvailable(false);
                continueButton?.SetAvailable(false);
                return;
            }
            var humanTurn = match.Phase == MatchPhase.Playing && match.ActiveSeat == PlayerSeat.South;
            playButton?.SetAvailable(humanTurn && selectedIds.Count > 0);
            passButton?.SetAvailable(humanTurn && match.CurrentPlay != null);
            hintButton?.SetAvailable(humanTurn);
            var canEnterTreasure = match.Phase == MatchPhase.HandComplete && raceOpen && !raceResolved;
            enterTreasureButton?.SetAvailable(canEnterTreasure && !treasureViewActive);
            steadyButton?.SetAvailable(canEnterTreasure && treasureViewActive);
            riskyButton?.SetAvailable(canEnterTreasure && treasureViewActive);
            if (!raceResolved) continueButton?.SetAvailable(false);
            if (phaseLabel != null)
            {
                phaseLabel.text = match.Phase switch
                {
                    MatchPhase.TributeReturn => "还贡 · 请选择一张牌",
                    MatchPhase.Playing => $"掼蛋 · 级牌 {GuandanMatchEngine.RankLabel(match.LevelRank)} · {GuandanMatchEngine.SeatLabel(match.ActiveSeat)}行动",
                    MatchPhase.HandComplete => "夺宝 · 选择探方路线",
                    MatchPhase.MatchComplete => "地宫宝箱已开启",
                    _ => "墓室牌局",
                };
            }
        }

        private void ShowMessage(string message)
        {
            this.message = message ?? string.Empty;
            if (messageLabel == null) return;
            messageLabel.text = message;
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ClearMessageSoon());
        }

        private IEnumerator ClearMessageSoon()
        {
            yield return new WaitForSeconds(4f);
            this.message = string.Empty;
            if (messageLabel != null) messageLabel.text = string.Empty;
            screenUi?.Refresh();
        }

        private void DisableLegacyWorldUi()
        {
            if (uiAnchor != null) uiAnchor.gameObject.SetActive(false);
            if (worldUiRoot != null) worldUiRoot.gameObject.SetActive(false);
            if (cardRoot != null) cardRoot.gameObject.SetActive(false);
            if (tablePlayRoot != null) tablePlayRoot.gameObject.SetActive(false);
            if (playerHandAnchor != null) playerHandAnchor.gameObject.SetActive(false);
            if (tablePlayAnchor != null) tablePlayAnchor.gameObject.SetActive(false);
            foreach (var view in GetComponentsInChildren<CardView>(true))
            {
                if (view != null) view.gameObject.SetActive(false);
            }
            foreach (var text in seatLabels.Values)
            {
                if (text != null) text.gameObject.SetActive(false);
            }
        }

        private void EnsureScreenUi()
        {
            screenUi = GetComponent<ScreenGameUi>() ?? gameObject.AddComponent<ScreenGameUi>();
            var camera = Camera.main ?? FindFirstObjectByType<Guandan.XR.GuandanXRBootstrap>()?.HeadCamera;
            screenUi.Initialize(this, camera);
            screenUi.SetSpatialSeatPlaques(true);
            Debug.Log($"[Guandan] 平面牌 UI 已启用；相机={(camera != null ? camera.name : "等待 XR 头部相机")}；桌面实体牌已禁用。");
        }

        private void BuildSpatialSeatStatusUi()
        {
            if (screenUi == null || seatAnchors == null || seatAnchors.Length < 4) return;
            var plaque = Resources.Load<Sprite>("GuandanUI/SeatPlaque");
            foreach (var seat in new[] { PlayerSeat.East, PlayerSeat.North, PlayerSeat.West })
            {
                if (worldSeatStatusUi.ContainsKey(seat)) continue;
                var anchor = seatAnchors[(int)seat];
                if (anchor == null) continue;
                worldSeatStatusUi[seat] = WorldSeatStatusUi.Create(this, screenUi, seat, anchor, plaque);
            }
        }

        private static IReadOnlyList<Card> OrderPatternCards(PlayPattern play)
        {
            if (play == null) return Array.Empty<Card>();
            var ascending = play.Kind is PlayKind.Straight or PlayKind.ConsecutivePairs or PlayKind.SteelPlate;
            var groups = play.Cards.GroupBy(card => EffectiveRank(card, play));
            var orderedGroups = play.Kind == PlayKind.FullHouse
                ? groups.OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key)
                : ascending
                    ? groups.OrderBy(group => group.Key)
                    : groups.OrderByDescending(group => group.Key);
            return orderedGroups
                .SelectMany(group => group.OrderBy(card => card.Suit).ThenBy(card => card.Id, StringComparer.Ordinal))
                .ToArray();
        }

        private static int EffectiveRank(Card card, PlayPattern play)
        {
            foreach (var wildcard in play.Wildcards)
            {
                if (wildcard.CardId == card.Id) return wildcard.RepresentedRank;
            }
            return card.Rank;
        }

        private static PlayerProfile[] CreateLotteryProfiles(int sourceSeed)
        {
            var pool = new[]
            {
                new PlayerProfile("沈砚", "谨慎的计算派", "习惯先看搭档的牌路，能稳就不冒险。复杂局面会思考很久，但很少冲动出炸。", "会轻轻敲桌，再把牌推到中央。", new[] { "沉稳", "重配合", "偏稳当" }, 0.14f, 0.12f, 1.6f, 5.2f),
                new PlayerProfile("唐果", "爱冒险的气氛组", "出牌节奏快，落后时尤其喜欢赌一把。收到鲜花会认真接住。", "会先看向宝箱，再迅速拍下手里的牌。", new[] { "活泼", "敢冒险", "爱鲜花" }, 0.76f, 0.42f, 0.65f, 2.0f),
                new PlayerProfile("秦峥", "强硬的压制派", "喜欢抢回牌权，炸弹出手干脆。赢下一轮后会向对面扬眉。", "被西红柿砸中后，大概率会立刻扔回来。", new[] { "强硬", "好胜", "爱压制" }, 0.58f, 0.70f, 0.9f, 3.1f),
                new PlayerProfile("苏禾", "温和的协作派", "会主动给搭档留出牌权，不轻易盖住队友。", "收到鲜花时会别在衣襟上，保持到本小局结束。", new[] { "温和", "顾搭档", "有耐心" }, 0.28f, 0.18f, 1.2f, 4.3f),
                new PlayerProfile("罗放", "爱热闹的挑衅派", "牌桌动作很多，喜欢用表情扰乱气氛，但真正出牌并不莽撞。", "最容易躲西红柿，也最容易捡起来扔回去。", new[] { "幽默", "爱互动", "会反击" }, 0.52f, 0.34f, 0.75f, 2.7f),
                new PlayerProfile("叶岚", "沉默的快打派", "喜欢尽快清理手牌，局势明朗时会突然提速。", "很少说话，但会用一张漂亮的顺子回应。", new[] { "安静", "快打", "看残局" }, 0.44f, 0.26f, 0.55f, 1.8f),
            };
            var random = new System.Random(sourceSeed);
            for (var i = pool.Length - 1; i > 0; i--)
            {
                var swap = random.Next(i + 1);
                (pool[i], pool[swap]) = (pool[swap], pool[i]);
            }
            return new[] { pool[0], pool[1], pool[2] };
        }

        private void ResetRandomStreams()
        {
            seed = useFixedDebugSeed ? debugSeed : CreateEntropySeed();
            sessionRandom = new System.Random(seed);
            aiRandom = new System.Random(NextSeed());
            raceRandom = new System.Random(NextSeed());
        }

        private int NextSeed()
        {
            if (sessionRandom == null) sessionRandom = new System.Random(CreateEntropySeed());
            return unchecked(sessionRandom.Next() ^ (sessionRandom.Next() << 16));
        }

        private static int CreateEntropySeed()
        {
            unchecked
            {
                var ticks = DateTime.UtcNow.Ticks;
                return Guid.NewGuid().GetHashCode()
                    ^ Environment.TickCount
                    ^ (int)ticks
                    ^ (int)(ticks >> 32);
            }
        }
    }
}
