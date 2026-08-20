using System;
using System.Collections.Generic;
using System.Linq;

namespace Guandan.Game
{
    public enum PlayerSeat
    {
        South = 0,
        East = 1,
        North = 2,
        West = 3,
    }

    public enum MatchPhase
    {
        Idle,
        Dealing,
        TributePayment,
        TributeAssignment,
        TributeReturn,
        Playing,
        HandComplete,
        MatchComplete,
    }

    public enum MatchEventType
    {
        HandStarted,
        AntiTribute,
        TributePaid,
        TributeAssigned,
        TributeReturned,
        CardsPlayed,
        Passed,
        TrickReset,
        PlayerFinished,
        HandFinished,
        Error,
    }

    public sealed class MatchEvent
    {
        public MatchEventType Type { get; set; }
        public PlayerSeat Seat { get; set; }
        public PlayerSeat OtherSeat { get; set; }
        public PlayPattern Play { get; set; }
        public Card Card { get; set; }
        public string Message { get; set; }
        public HandResult HandResult { get; set; }
    }

    public sealed class HandResult
    {
        public IReadOnlyList<PlayerSeat> Placements { get; set; }
        public int WinningTeam { get; set; }
        public int BaseSteps { get; set; }
        public PlayerSeat FirstSeat => Placements[0];
    }

    public sealed class TributeExchange
    {
        public PlayerSeat Payer { get; set; }
        public PlayerSeat Recipient { get; set; }
        public Card TributeCard { get; set; }
        public Card ReturnCard { get; set; }
        public bool IsReturned => ReturnCard != null;
    }

    public sealed class GuandanMatchEngine
    {
        public static readonly PlayerSeat[] AllSeats =
        {
            PlayerSeat.South,
            PlayerSeat.East,
            PlayerSeat.North,
            PlayerSeat.West,
        };

        private readonly Dictionary<PlayerSeat, List<Card>> hands = new();
        private readonly List<PlayerSeat> finishOrder = new();
        private readonly List<TributeExchange> tributeExchanges = new();
        private readonly int[] teamLevels = { 2, 2 };
        private Random random;
        private int consecutivePasses;
        private List<PlayerSeat> previousPlacements = new();
        private PlayerSeat? tributeOpeningLeader;

        public event Action<MatchEvent> EventRaised;
        public event Action StateChanged;

        public MatchPhase Phase { get; private set; } = MatchPhase.Idle;
        public int HandNumber { get; private set; }
        public int LevelRank { get; private set; } = 2;
        public PlayerSeat ActiveSeat { get; private set; } = PlayerSeat.South;
        public PlayPattern CurrentPlay { get; private set; }
        public PlayerSeat CurrentPlaySeat { get; private set; }
        public IReadOnlyList<PlayerSeat> FinishOrder => finishOrder;
        public IReadOnlyList<TributeExchange> TributeExchanges => tributeExchanges;
        public HandResult LastHandResult { get; private set; }

        public GuandanMatchEngine(int seed)
        {
            random = new Random(seed);
            foreach (var seat in AllSeats) hands[seat] = new List<Card>(27);
        }

        public static int TeamOf(PlayerSeat seat) => (int)seat % 2;
        public static PlayerSeat PartnerOf(PlayerSeat seat) => (PlayerSeat)(((int)seat + 2) % 4);

        public IReadOnlyList<Card> GetHand(PlayerSeat seat) => hands[seat];
        public bool HasFinished(PlayerSeat seat) => finishOrder.Contains(seat);
        public int GetTeamLevel(int team) => teamLevels[Math.Clamp(team, 0, 1)];

        public void StartMatch()
        {
            previousPlacements.Clear();
            teamLevels[0] = 2;
            teamLevels[1] = 2;
            LevelRank = 2;
            HandNumber = 0;
            LastHandResult = null;
            StartNextHand();
        }

        public void StartNextHand()
        {
            if (Phase == MatchPhase.MatchComplete) return;
            HandNumber++;
            Phase = MatchPhase.Dealing;
            CurrentPlay = null;
            consecutivePasses = 0;
            finishOrder.Clear();
            tributeExchanges.Clear();
            tributeOpeningLeader = null;
            foreach (var seat in AllSeats) hands[seat].Clear();

            var deck = Deck.CreateDoubleDeck();
            Deck.Shuffle(deck, random);
            Card markerCard = null;
            if (previousPlacements.Count == 0)
            {
                markerCard = deck[0];
                var cutIndex = random.Next(1, deck.Count - 1);
                deck = deck.Skip(cutIndex).Concat(deck.Take(cutIndex)).ToList();
            }
            var firstDrawer = FirstDrawerForNextHand();
            var firstDrawerIndex = Array.IndexOf(AllSeats, firstDrawer);
            var dealOrder = Enumerable.Range(0, AllSeats.Length)
                .Select(offset => AllSeats[(firstDrawerIndex + offset) % AllSeats.Length])
                .ToArray();
            var markerOwner = firstDrawer;
            for (var i = 0; i < deck.Count; i++)
            {
                var owner = dealOrder[i % dealOrder.Length];
                hands[owner].Add(deck[i]);
                if (markerCard != null && deck[i].Id == markerCard.Id) markerOwner = owner;
            }
            foreach (var seat in AllSeats) SortHand(hands[seat]);

            ActiveSeat = previousPlacements.Count == 0 ? markerOwner : firstDrawer;
            Raise(MatchEventType.HandStarted, ActiveSeat, $"第 {HandNumber} 局，级牌 {RankLabel(LevelRank)}");
            NotifyState();
        }

        public IReadOnlyList<PlayPattern> GetLegalPlays(PlayerSeat seat)
        {
            if (Phase != MatchPhase.Playing || seat != ActiveSeat || HasFinished(seat)) return Array.Empty<PlayPattern>();
            return GuandanRuleEngine.GetLegalPlays(hands[seat], LevelRank, CurrentPlay);
        }

        public IReadOnlyList<Card> GetLegalReturnCards(PlayerSeat seat)
        {
            if (Phase != MatchPhase.TributeReturn || seat != ActiveSeat) return Array.Empty<Card>();
            var exchange = tributeExchanges.FirstOrDefault(item => item.Recipient == seat && !item.IsReturned);
            if (exchange == null) return Array.Empty<Card>();
            return hands[seat]
                .Where(card => card.Id != exchange.TributeCard.Id)
                .OrderBy(card => card.Power(LevelRank))
                .ThenBy(card => card.Suit)
                .ToArray();
        }

        public IReadOnlyList<Card> GetLegalTributeCards(PlayerSeat seat)
        {
            if (Phase != MatchPhase.TributePayment || seat != ActiveSeat) return Array.Empty<Card>();
            var exchange = tributeExchanges.FirstOrDefault(item => item.Payer == seat && item.TributeCard == null);
            if (exchange == null) return Array.Empty<Card>();
            var eligible = hands[seat].Where(card => !card.IsWildcard(LevelRank)).ToArray();
            if (eligible.Length == 0) return Array.Empty<Card>();
            var strongest = eligible.Max(card => card.Power(LevelRank));
            return eligible
                .Where(card => card.Power(LevelRank) == strongest)
                .OrderBy(card => card.Id, StringComparer.Ordinal)
                .ToArray();
        }

        public void BeginAfterDealPresentation()
        {
            if (Phase == MatchPhase.Dealing) PrepareTributeOrBeginPlay();
        }

        public bool PayTribute(PlayerSeat seat, Card card)
        {
            if (!GetLegalTributeCards(seat).Any(legal => legal.Id == card?.Id))
                return Reject(seat, "这张牌不是当前可进贡的最大牌。\n");

            var exchange = tributeExchanges.First(item => item.Payer == seat && item.TributeCard == null);
            hands[seat].RemoveAll(item => item.Id == card.Id);
            exchange.TributeCard = card;
            Raise(MatchEventType.TributePaid, seat, $"{SeatLabel(seat)}完成进贡", card: card);

            var unpaid = tributeExchanges.FirstOrDefault(item => item.TributeCard == null);
            if (unpaid != null)
            {
                ActiveSeat = unpaid.Payer;
                NotifyState();
                return true;
            }

            if (tributeExchanges.Count == 1)
            {
                AssignTributes(
                    new[] { (tributeExchanges[0].Payer, previousPlacements[0]) },
                    tributeExchanges[0].Payer);
                return true;
            }

            var first = tributeExchanges[0];
            var second = tributeExchanges[1];
            var comparison = first.TributeCard.Power(LevelRank).CompareTo(second.TributeCard.Power(LevelRank));
            if (comparison == 0)
            {
                Phase = MatchPhase.TributeAssignment;
                ActiveSeat = previousPlacements[0];
                NotifyState();
                return true;
            }

            var higher = comparison > 0 ? first : second;
            var lower = comparison > 0 ? second : first;
            AssignTributes(
                new[]
                {
                    (higher.Payer, previousPlacements[0]),
                    (lower.Payer, previousPlacements[1]),
                },
                higher.Payer);
            return true;
        }

        public bool AssignEqualTributes()
        {
            if (Phase != MatchPhase.TributeAssignment || tributeExchanges.Count != 2)
                return Reject(ActiveSeat, "当前不需要分配同点进贡。\n");

            AssignTributes(
                new[]
                {
                    (tributeExchanges[0].Payer, previousPlacements[0]),
                    (tributeExchanges[1].Payer, previousPlacements[1]),
                },
                tributeExchanges[0].Payer);
            return true;
        }

        public bool ReturnTribute(PlayerSeat seat, Card card)
        {
            if (!GetLegalReturnCards(seat).Any(legal => legal.Id == card?.Id)) return Reject(seat, "这张牌不能用于还贡。\n");
            var exchange = tributeExchanges.First(item => item.Recipient == seat && !item.IsReturned);
            MoveCard(card, seat, exchange.Payer);
            exchange.ReturnCard = card;
            Raise(MatchEventType.TributeReturned, seat, $"{SeatLabel(seat)}向{SeatLabel(exchange.Payer)}还贡", exchange.Payer, card: card);
            ContinueTributeReturnsOrBeginPlay();
            return true;
        }

        public bool Play(PlayerSeat seat, PlayPattern requestedPlay)
        {
            if (Phase != MatchPhase.Playing || seat != ActiveSeat) return Reject(seat, "还没轮到该玩家。\n");
            if (requestedPlay == null) return Reject(seat, "没有选择有效牌型。\n");
            var selected = requestedPlay.Cards
                .Select(card => hands[seat].FirstOrDefault(item => item.Id == card.Id))
                .Where(card => card != null)
                .ToArray();
            if (selected.Length != requestedPlay.Cards.Count) return Reject(seat, "手牌状态已经变化。\n");
            var legalVariants = GuandanRuleEngine.FindPatternsForSelection(selected, LevelRank, CurrentPlay);
            var legal = legalVariants.FirstOrDefault(play => play.Identity == requestedPlay.Identity) ?? legalVariants.FirstOrDefault();
            if (legal == null) return Reject(seat, "所选牌不能压过桌面牌型。\n");

            foreach (var card in legal.Cards) hands[seat].RemoveAll(item => item.Id == card.Id);
            CurrentPlay = legal;
            CurrentPlaySeat = seat;
            consecutivePasses = 0;
            Raise(MatchEventType.CardsPlayed, seat, $"{SeatLabel(seat)}打出{legal.Label}", play: legal);

            if (hands[seat].Count == 0)
            {
                finishOrder.Add(seat);
                Raise(MatchEventType.PlayerFinished, seat, $"{SeatLabel(seat)}获得第 {finishOrder.Count} 名");
                if (ShouldFinishHand())
                {
                    FinishHand();
                    return true;
                }
            }

            ActiveSeat = NextPlayableSeat(seat);
            NotifyState();
            return true;
        }

        public bool Pass(PlayerSeat seat)
        {
            if (Phase != MatchPhase.Playing || seat != ActiveSeat || CurrentPlay == null) return Reject(seat, "当前不能不出。\n");
            consecutivePasses++;
            Raise(MatchEventType.Passed, seat, $"{SeatLabel(seat)}不出");

            var activeCount = AllSeats.Count(other => !HasFinished(other));
            var ownerStillActive = !HasFinished(CurrentPlaySeat);
            var passesNeeded = activeCount - (ownerStillActive ? 1 : 0);
            if (consecutivePasses >= Math.Max(1, passesNeeded))
            {
                var leader = ownerStillActive ? CurrentPlaySeat : NextPlayableSeat(CurrentPlaySeat);
                CurrentPlay = null;
                consecutivePasses = 0;
                ActiveSeat = leader;
                Raise(MatchEventType.TrickReset, leader, $"新一轮由{SeatLabel(leader)}领出");
            }
            else
            {
                ActiveSeat = NextPlayableSeat(seat);
            }

            NotifyState();
            return true;
        }

        public void MarkMatchComplete()
        {
            Phase = MatchPhase.MatchComplete;
            NotifyState();
        }

        private void PrepareTributeOrBeginPlay()
        {
            if (previousPlacements.Count != 4)
            {
                BeginPlay();
                return;
            }

            var first = previousPlacements[0];
            var second = previousPlacements[1];
            var fourth = previousPlacements[3];
            if (TeamOf(first) == TeamOf(second))
            {
                foreach (var payer in AllSeats.Where(seat => TeamOf(seat) != TeamOf(first)))
                    tributeExchanges.Add(new TributeExchange { Payer = payer });
            }
            else
            {
                tributeExchanges.Add(new TributeExchange { Payer = fourth });
            }

            var payers = tributeExchanges.Select(exchange => exchange.Payer).Distinct().ToArray();
            var bigJokers = payers.Sum(seat => hands[seat].Count(card => card.IsBigJoker));
            if (bigJokers >= 2)
            {
                tributeExchanges.Clear();
                Raise(MatchEventType.AntiTribute, payers[0], "两张大王抗贡，本局免贡");
                BeginPlay();
                return;
            }

            Phase = MatchPhase.TributePayment;
            ActiveSeat = tributeExchanges[0].Payer;
            NotifyState();
        }

        private void AssignTributes(
            IReadOnlyList<(PlayerSeat Payer, PlayerSeat Recipient)> assignments,
            PlayerSeat openingLeader)
        {
            tributeOpeningLeader = openingLeader;
            foreach (var assignment in assignments)
            {
                var exchange = tributeExchanges.First(item => item.Payer == assignment.Payer);
                exchange.Recipient = assignment.Recipient;
                hands[assignment.Recipient].Add(exchange.TributeCard);
                SortHand(hands[assignment.Recipient]);
                Raise(
                    MatchEventType.TributeAssigned,
                    assignment.Payer,
                    $"{SeatLabel(assignment.Payer)}向{SeatLabel(assignment.Recipient)}进贡",
                    assignment.Recipient,
                    card: exchange.TributeCard);
            }

            Phase = MatchPhase.TributeReturn;
            ActiveSeat = previousPlacements[0];
            NotifyState();
        }

        private void ContinueTributeReturnsOrBeginPlay()
        {
            var pending = tributeExchanges.FirstOrDefault(exchange => !exchange.IsReturned);
            if (pending != null)
            {
                ActiveSeat = pending.Recipient;
                SortHand(hands[pending.Recipient]);
                NotifyState();
                return;
            }

            ActiveSeat = tributeOpeningLeader ?? tributeExchanges[0].Payer;
            foreach (var seat in AllSeats) SortHand(hands[seat]);
            BeginPlay();
        }

        private void BeginPlay()
        {
            Phase = MatchPhase.Playing;
            NotifyState();
        }

        private bool ShouldFinishHand()
        {
            if (finishOrder.Count >= 2 && TeamOf(finishOrder[0]) == TeamOf(finishOrder[1])) return true;
            return finishOrder.Count >= 3;
        }

        private void FinishHand()
        {
            foreach (var seat in AllSeats
                         .Where(seat => !finishOrder.Contains(seat))
                         .OrderBy(seat => hands[seat].Count)
                         .ThenBy(seat => seat))
            {
                finishOrder.Add(seat);
            }

            var winningTeam = TeamOf(finishOrder[0]);
            var partnerPosition = finishOrder.IndexOf(PartnerOf(finishOrder[0]));
            var baseSteps = partnerPosition switch
            {
                1 => 3,
                2 => 2,
                _ => 1,
            };
            teamLevels[winningTeam] = AdvanceRank(teamLevels[winningTeam], baseSteps);
            LevelRank = teamLevels[winningTeam];
            previousPlacements = finishOrder.ToList();
            LastHandResult = new HandResult
            {
                Placements = previousPlacements.ToArray(),
                WinningTeam = winningTeam,
                BaseSteps = baseSteps,
            };
            Phase = MatchPhase.HandComplete;
            Raise(MatchEventType.HandFinished, finishOrder[0], $"{(winningTeam == 0 ? "青队" : "朱队")}推进 {baseSteps} 格", handResult: LastHandResult);
            NotifyState();
        }

        private PlayerSeat NextPlayableSeat(PlayerSeat after)
        {
            for (var offset = 1; offset <= 4; offset++)
            {
                var candidate = (PlayerSeat)(((int)after + offset) % 4);
                if (!HasFinished(candidate)) return candidate;
            }
            return after;
        }

        private PlayerSeat FirstDrawerForNextHand()
        {
            if (previousPlacements.Count != 4) return PlayerSeat.South;
            var first = previousPlacements[0];
            var second = previousPlacements[1];
            return TeamOf(first) == TeamOf(second)
                ? AllSeats.First(seat => TeamOf(seat) != TeamOf(first))
                : previousPlacements[3];
        }

        private void MoveCard(Card card, PlayerSeat from, PlayerSeat to)
        {
            hands[from].RemoveAll(item => item.Id == card.Id);
            hands[to].Add(card);
        }

        private void SortHand(List<Card> hand)
        {
            hand.Sort((first, second) =>
            {
                var power = first.Power(LevelRank).CompareTo(second.Power(LevelRank));
                return power != 0 ? power : first.Suit.CompareTo(second.Suit);
            });
        }

        private bool Reject(PlayerSeat seat, string message)
        {
            Raise(MatchEventType.Error, seat, message.Trim());
            return false;
        }

        private void Raise(
            MatchEventType type,
            PlayerSeat seat,
            string message,
            PlayerSeat otherSeat = PlayerSeat.South,
            PlayPattern play = null,
            Card card = null,
            HandResult handResult = null)
        {
            EventRaised?.Invoke(new MatchEvent
            {
                Type = type,
                Seat = seat,
                OtherSeat = otherSeat,
                Play = play,
                Card = card,
                Message = message,
                HandResult = handResult,
            });
        }

        private void NotifyState()
        {
            StateChanged?.Invoke();
        }

        private static int AdvanceRank(int rank, int steps) => Math.Min(14, rank + Math.Max(0, steps));

        public static string RankLabel(int rank) => rank switch
        {
            11 => "J",
            12 => "Q",
            13 => "K",
            14 => "A",
            _ => rank.ToString(),
        };

        public static string SeatLabel(PlayerSeat seat) => seat switch
        {
            PlayerSeat.South => "你",
            PlayerSeat.East => "东家",
            PlayerSeat.North => "队友",
            PlayerSeat.West => "西家",
            _ => seat.ToString(),
        };
    }
}
