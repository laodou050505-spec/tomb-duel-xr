using System;
using System.Collections.Generic;
using System.Linq;

namespace Guandan.Game
{
    [Serializable]
    public sealed class AiPersonality
    {
        public string Name;
        public float RiskBias;
        public float BombBias;
        public float ThinkMin;
        public float ThinkMax;

        public AiPersonality(string name, float riskBias, float bombBias, float thinkMin, float thinkMax)
        {
            Name = name;
            RiskBias = riskBias;
            BombBias = bombBias;
            ThinkMin = thinkMin;
            ThinkMax = thinkMax;
        }
    }

    public static class GuandanAI
    {
        public static PlayPattern ChoosePlay(GuandanMatchEngine match, PlayerSeat seat, AiPersonality personality, Random random)
        {
            var legal = match.GetLegalPlays(seat).ToList();
            if (legal.Count == 0) return null;
            var hand = match.GetHand(seat);
            var handCount = hand.Count;
            var finishing = legal.FirstOrDefault(play => play.Cards.Count == handCount);
            if (finishing != null) return finishing;

            var partnerOwnsTable = match.CurrentPlay != null
                && GuandanMatchEngine.TeamOf(match.CurrentPlaySeat) == GuandanMatchEngine.TeamOf(seat);
            var partner = GuandanMatchEngine.PartnerOf(seat);
            var teammateCount = match.GetHand(partner).Count;
            var enemyCounts = GuandanMatchEngine.AllSeats
                .Where(other => GuandanMatchEngine.TeamOf(other) != GuandanMatchEngine.TeamOf(seat))
                .Select(other => match.GetHand(other).Count)
                .ToArray();
            var enemyMin = enemyCounts.Length > 0 ? enemyCounts.Min() : 27;
            var tableOwnerCount = match.CurrentPlay != null ? match.GetHand(match.CurrentPlaySeat).Count : 27;
            var enemyThreat = !partnerOwnsTable && tableOwnerCount <= 5;
            var ownEndgame = handCount <= 8;

            if (match.CurrentPlay != null
                && partnerOwnsTable
                && (teammateCount <= 8 || !ownEndgame || enemyMin > 2))
                return null;

            var nonBombs = legal.Where(play => !play.IsBomb).ToList();
            if (match.CurrentPlay != null)
            {
                var spendBomb = enemyThreat
                    || enemyMin <= 3
                    || ownEndgame
                    || random.NextDouble() < personality.BombBias * 0.18;
                var pool = nonBombs.Count > 0 ? nonBombs : spendBomb ? legal : new List<PlayPattern>();
                return ChooseBestCandidate(
                    pool,
                    hand,
                    match.LevelRank,
                    false,
                    random,
                    enemyThreat || enemyMin <= 3,
                    personality.RiskBias);
            }

            var leadPool = nonBombs.Count > 0 ? nonBombs : legal;
            var scored = leadPool
                .Select((candidate, order) => (Candidate: candidate, Score: ScoreCandidate(candidate, hand, match.LevelRank, order, true)))
                .OrderByDescending(entry => entry.Score)
                .ToArray();
            var close = scored.Where(entry => entry.Score >= scored[0].Score - 8).ToArray();
            var choiceWindow = Math.Max(1, Math.Min(close.Length, personality.RiskBias > 0.6f ? 3 : 2));
            return close[random.Next(choiceWindow)].Candidate;
        }

        public static int ScoreCandidate(
            PlayPattern candidate,
            IReadOnlyList<Card> hand,
            int levelRank,
            int order,
            bool isLead)
        {
            if (candidate == null) return int.MinValue;
            var shape = candidate.Kind switch
            {
                PlayKind.Pair => 16,
                PlayKind.Triple => 24,
                PlayKind.FullHouse => 72,
                PlayKind.Straight => 78,
                PlayKind.ConsecutivePairs => 88,
                PlayKind.SteelPlate => 96,
                PlayKind.Bomb => 44,
                PlayKind.StraightFlush => 54,
                PlayKind.JokerBomb => 62,
                _ => 0,
            };
            var bombCost = candidate.IsBomb ? (isLead ? 140 : 62) : 0;
            var weaknessBonus = isLead ? 0 : Math.Max(0, 36 - order * 2);
            return candidate.Cards.Count * 22
                + shape
                + weaknessBonus
                - EstimateRemainingTurns(hand, candidate, levelRank) * 28
                - BreakPenalty(hand, candidate, levelRank)
                - bombCost;
        }

        private static PlayPattern ChooseBestCandidate(
            IReadOnlyList<PlayPattern> candidates,
            IReadOnlyList<Card> hand,
            int levelRank,
            bool isLead,
            Random random,
            bool enemyThreat,
            float riskBias)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var scored = candidates
                .Select((candidate, order) =>
                {
                    var pressure = enemyThreat ? candidate.Cards.Count * 5 + (candidate.IsBomb ? 46 : 0) : 0;
                    return (Candidate: candidate, Score: ScoreCandidate(candidate, hand, levelRank, order, isLead) + pressure);
                })
                .OrderByDescending(entry => entry.Score)
                .ToArray();
            var tolerance = enemyThreat ? 2 : riskBias > 0.62f ? 6 : 4;
            var close = scored.Where(entry => entry.Score >= scored[0].Score - tolerance).Take(3).ToArray();
            if (close.Length == 1 || random.NextDouble() < 0.76) return close[0].Candidate;
            return close[random.Next(1, close.Length)].Candidate;
        }

        private static int EstimateRemainingTurns(IReadOnlyList<Card> hand, PlayPattern candidate, int levelRank)
        {
            var used = new HashSet<string>(candidate.Cards.Select(card => card.Id), StringComparer.Ordinal);
            var remaining = hand.Where(card => !used.Contains(card.Id)).ToArray();
            if (remaining.Length == 0) return 0;
            var groups = remaining.GroupBy(card => GroupKey(card, levelRank)).ToArray();
            var naturalRanks = remaining
                .Where(card => !card.IsJoker && !card.IsWildcard(levelRank))
                .GroupBy(card => card.Rank)
                .Select(group => group.Key)
                .OrderBy(rank => rank)
                .ToArray();
            var turns = groups.Length;
            var run = 1;
            for (var index = 1; index < naturalRanks.Length; index++)
            {
                if (naturalRanks[index] == naturalRanks[index - 1] + 1)
                {
                    run++;
                    if (run == 5) turns -= 4;
                }
                else run = 1;
            }
            return Math.Max(1, turns);
        }

        private static int BreakPenalty(IReadOnlyList<Card> hand, PlayPattern candidate, int levelRank)
        {
            if (candidate.IsBomb) return candidate.Wildcards.Count * 4;
            var groups = hand.GroupBy(card => GroupKey(card, levelRank))
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var used = candidate.Cards.GroupBy(card => GroupKey(card, levelRank))
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var penalty = 0;
            foreach (var pair in used)
            {
                var total = groups.TryGetValue(pair.Key, out var count) ? count : pair.Value;
                if (total >= 4 && pair.Value < total) penalty += 72;
                else if (total == 3 && pair.Value < total) penalty += 20;
                else if (total == 2 && pair.Value < total) penalty += 12;
            }
            return penalty + candidate.Wildcards.Count * 13;
        }

        private static string GroupKey(Card card, int levelRank)
        {
            if (card.IsJoker) return $"joker:{card.Rank}";
            if (card.IsWildcard(levelRank)) return $"wild:{card.Id}";
            return $"rank:{card.Rank}";
        }

        public static Card ChooseReturnTribute(IReadOnlyList<Card> legalCards, int levelRank)
        {
            return legalCards
                .OrderBy(card => card.Power(levelRank))
                .ThenBy(card => card.Suit)
                .FirstOrDefault();
        }

        public static TreasureRoute ChooseRoute(AiPersonality personality, TreasureRace race, int team, int baseSteps, Random random)
        {
            var own = race.GetPosition(team);
            var opponent = race.GetPosition(team == 0 ? 1 : 0);
            if (own + baseSteps >= TreasureRace.TrackLength) return TreasureRoute.Steady;
            var behindBoost = opponent - own >= 3 ? 0.18 : 0.0;
            var finalStretchPenalty = own >= 8 ? 0.16 : 0.0;
            var chance = Math.Max(0.06, personality.RiskBias + behindBoost - finalStretchPenalty);
            return random.NextDouble() < chance ? TreasureRoute.Risky : TreasureRoute.Steady;
        }
    }
}
