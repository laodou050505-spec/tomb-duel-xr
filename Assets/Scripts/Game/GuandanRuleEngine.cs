using System;
using System.Collections.Generic;
using System.Linq;

namespace Guandan.Game
{
    public static class GuandanRuleEngine
    {
        private sealed class SelectionOption
        {
            public List<Card> Cards { get; } = new();
            public List<WildcardUse> Wildcards { get; } = new();
        }

        public static IReadOnlyList<PlayPattern> GetLegalPlays(
            IReadOnlyList<Card> hand,
            int levelRank,
            PlayPattern previousPlay = null)
        {
            return EnumeratePlays(hand, levelRank)
                .Where(play => previousPlay == null || CanBeat(play, previousPlay, levelRank))
                .OrderBy(play => play.IsBomb ? 1 : 0)
                .ThenBy(play => play.Cards.Count)
                .ThenBy(play => Strength(play, levelRank))
                .ToArray();
        }

        public static IReadOnlyList<PlayPattern> FindPatternsForSelection(
            IReadOnlyList<Card> selectedCards,
            int levelRank,
            PlayPattern previousPlay = null)
        {
            var selectedIds = selectedCards.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return EnumeratePlays(selectedCards, levelRank)
                .Where(play => play.Cards.Count == selectedCards.Count)
                .Where(play => play.Cards.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(selectedIds))
                .Where(play => previousPlay == null || CanBeat(play, previousPlay, levelRank))
                .OrderBy(play => Strength(play, levelRank))
                .ToArray();
        }

        public static bool CanBeat(PlayPattern candidate, PlayPattern previousPlay, int levelRank)
        {
            if (candidate == null || candidate.Kind == PlayKind.Invalid) return false;
            if (previousPlay == null) return true;

            if (candidate.IsBomb && !previousPlay.IsBomb) return true;
            if (!candidate.IsBomb && previousPlay.IsBomb) return false;
            if (candidate.IsBomb && previousPlay.IsBomb)
            {
                return BombStrength(candidate, levelRank) > BombStrength(previousPlay, levelRank);
            }

            if (candidate.Kind != previousPlay.Kind || candidate.Cards.Count != previousPlay.Cards.Count) return false;
            return Strength(candidate, levelRank) > Strength(previousPlay, levelRank);
        }

        public static int Strength(PlayPattern play, int levelRank)
        {
            if (play == null) return int.MinValue;
            if (play.IsBomb) return BombStrength(play, levelRank);
            if (play.Kind == PlayKind.Straight || play.Kind == PlayKind.ConsecutivePairs || play.Kind == PlayKind.SteelPlate)
            {
                return play.SequenceHighRank;
            }

            return RankPower(play.MainRank, levelRank);
        }

        public static int RankPower(int rank, int levelRank)
        {
            if (rank == 16) return 17;
            if (rank == 15) return 16;
            return rank == levelRank ? 15 : rank;
        }

        private static int BombStrength(PlayPattern play, int levelRank)
        {
            if (play.Kind == PlayKind.JokerBomb) return 900000;
            if (play.Kind == PlayKind.StraightFlush) return 300000 + SequencePower(play.SequenceHighRank);
            var tier = play.Cards.Count >= 6 ? 400000 : play.Cards.Count == 5 ? 200000 : 100000;
            var length = play.Cards.Count >= 6 ? play.Cards.Count * 100 : 0;
            return tier + length + RankPower(play.MainRank, levelRank);
        }

        private static int SequencePower(int rank)
        {
            return rank == 5 ? 3 : rank;
        }

        private static IReadOnlyList<PlayPattern> EnumeratePlays(IReadOnlyList<Card> hand, int levelRank)
        {
            var results = new Dictionary<string, PlayPattern>(StringComparer.Ordinal);
            void Add(PlayPattern play)
            {
                if (play != null && play.Kind != PlayKind.Invalid) results.TryAdd(play.Identity, play);
            }

            foreach (var card in hand)
            {
                Add(new PlayPattern(PlayKind.Single, new[] { card }, card.Rank));
            }

            for (var rank = 2; rank <= 16; rank++)
            {
                foreach (var option in RankOptions(hand, rank, 2, levelRank))
                {
                    Add(new PlayPattern(PlayKind.Pair, option.Cards, rank, wildcards: option.Wildcards));
                }

                foreach (var option in RankOptions(hand, rank, 3, levelRank))
                {
                    Add(new PlayPattern(PlayKind.Triple, option.Cards, rank, wildcards: option.Wildcards));
                }
            }

            for (var rank = 2; rank <= 14; rank++)
            {
                for (var count = 4; count <= 10; count++)
                {
                    foreach (var option in RankOptions(hand, rank, count, levelRank))
                    {
                        Add(new PlayPattern(PlayKind.Bomb, option.Cards, rank, wildcards: option.Wildcards));
                    }
                }
            }

            var jokers = hand.Where(card => card.IsJoker).ToArray();
            if (jokers.Length == 4 && jokers.Count(card => card.IsSmallJoker) == 2 && jokers.Count(card => card.IsBigJoker) == 2)
            {
                Add(new PlayPattern(PlayKind.JokerBomb, jokers, 16));
            }

            AddFullHouses(hand, levelRank, Add);
            AddSequences(hand, levelRank, PlayKind.Straight, 5, 1, Add);
            AddSequences(hand, levelRank, PlayKind.ConsecutivePairs, 3, 2, Add);
            AddSequences(hand, levelRank, PlayKind.SteelPlate, 2, 3, Add);
            AddStraightFlushes(hand, levelRank, Add);

            return results.Values.ToArray();
        }

        private static void AddFullHouses(IReadOnlyList<Card> hand, int levelRank, Action<PlayPattern> add)
        {
            for (var tripleRank = 2; tripleRank <= 14; tripleRank++)
            {
                var tripleOptions = RankOptions(hand, tripleRank, 3, levelRank);
                if (tripleOptions.Count == 0) continue;
                for (var pairRank = 2; pairRank <= 16; pairRank++)
                {
                    if (pairRank == tripleRank) continue;
                    var pairOptions = RankOptions(hand, pairRank, 2, levelRank);
                    foreach (var triple in tripleOptions)
                    {
                        foreach (var pair in pairOptions)
                        {
                            if (Overlaps(triple.Cards, pair.Cards)) continue;
                            add(new PlayPattern(
                                PlayKind.FullHouse,
                                triple.Cards.Concat(pair.Cards),
                                tripleRank,
                                wildcards: triple.Wildcards.Concat(pair.Wildcards)));
                        }
                    }
                }
            }
        }

        private static void AddSequences(
            IReadOnlyList<Card> hand,
            int levelRank,
            PlayKind kind,
            int rankCount,
            int cardsPerRank,
            Action<PlayPattern> add)
        {
            var maxStart = 14 - rankCount + 1;
            for (var start = 2; start <= maxStart; start++)
            {
                var ranks = Enumerable.Range(start, rankCount).ToArray();
                foreach (var option in CompoundRankOptions(hand, ranks, cardsPerRank, levelRank, null))
                {
                    if (kind == PlayKind.Straight
                        && option.Wildcards.Count == 0
                        && option.Cards.Select(card => card.Suit).Distinct().Count() == 1)
                        continue;
                    add(new PlayPattern(kind, option.Cards, ranks[^1], ranks[^1], option.Wildcards));
                }
            }

            var lowAce = kind switch
            {
                PlayKind.Straight => new[] { 14, 2, 3, 4, 5 },
                PlayKind.ConsecutivePairs => new[] { 14, 2, 3 },
                PlayKind.SteelPlate => new[] { 14, 2 },
                _ => Array.Empty<int>(),
            };
            if (lowAce.Length > 0)
            {
                foreach (var option in CompoundRankOptions(hand, lowAce, cardsPerRank, levelRank, null))
                {
                    if (kind == PlayKind.Straight
                        && option.Wildcards.Count == 0
                        && option.Cards.Select(card => card.Suit).Distinct().Count() == 1)
                        continue;
                    add(new PlayPattern(kind, option.Cards, lowAce[^1], lowAce[^1], option.Wildcards));
                }
            }
        }

        private static void AddStraightFlushes(IReadOnlyList<Card> hand, int levelRank, Action<PlayPattern> add)
        {
            foreach (var suit in new[] { CardSuit.Clubs, CardSuit.Diamonds, CardSuit.Hearts, CardSuit.Spades })
            {
                for (var start = 2; start <= 10; start++)
                {
                    var ranks = Enumerable.Range(start, 5).ToArray();
                    foreach (var option in CompoundRankOptions(hand, ranks, 1, levelRank, suit))
                    {
                        add(new PlayPattern(PlayKind.StraightFlush, option.Cards, ranks[^1], ranks[^1], option.Wildcards));
                    }
                }

                var wheel = new[] { 14, 2, 3, 4, 5 };
                foreach (var option in CompoundRankOptions(hand, wheel, 1, levelRank, suit))
                {
                    add(new PlayPattern(PlayKind.StraightFlush, option.Cards, 5, 5, option.Wildcards));
                }
            }
        }

        private static List<SelectionOption> RankOptions(
            IReadOnlyList<Card> hand,
            int rank,
            int count,
            int levelRank,
            CardSuit? requiredSuit = null)
        {
            var naturals = hand.Where(card =>
                card.Rank == rank
                && !card.IsWildcard(levelRank)
                && (!requiredSuit.HasValue || card.Suit == requiredSuit.Value))
                .ToArray();
            var wildcards = rank <= 14
                ? hand.Where(card => card.IsWildcard(levelRank)).ToArray()
                : Array.Empty<Card>();
            var options = new List<SelectionOption>();

            for (var wildCount = 0; wildCount <= Math.Min(count, wildcards.Length); wildCount++)
            {
                var naturalCount = count - wildCount;
                if (naturals.Length < naturalCount) continue;
                foreach (var naturalSet in Combinations(naturals, naturalCount))
                {
                    foreach (var wildSet in Combinations(wildcards, wildCount))
                    {
                        var option = new SelectionOption();
                        option.Cards.AddRange(naturalSet);
                        option.Cards.AddRange(wildSet);
                        option.Wildcards.AddRange(wildSet.Select(card => new WildcardUse(card.Id, rank, requiredSuit ?? card.Suit)));
                        options.Add(option);
                    }
                }
            }

            return options;
        }

        private static List<SelectionOption> CompoundRankOptions(
            IReadOnlyList<Card> hand,
            IReadOnlyList<int> ranks,
            int cardsPerRank,
            int levelRank,
            CardSuit? requiredSuit)
        {
            var byRank = ranks.Select(rank => RankOptions(hand, rank, cardsPerRank, levelRank, requiredSuit)).ToArray();
            if (byRank.Any(options => options.Count == 0)) return new List<SelectionOption>();

            var results = new List<SelectionOption>();
            Walk(0, new HashSet<string>(StringComparer.Ordinal), new SelectionOption());
            return results;

            void Walk(int index, HashSet<string> used, SelectionOption merged)
            {
                if (index == byRank.Length)
                {
                    results.Add(merged);
                    return;
                }

                foreach (var option in byRank[index])
                {
                    if (option.Cards.Any(card => used.Contains(card.Id))) continue;
                    var nextUsed = new HashSet<string>(used, StringComparer.Ordinal);
                    foreach (var card in option.Cards) nextUsed.Add(card.Id);
                    var next = new SelectionOption();
                    next.Cards.AddRange(merged.Cards);
                    next.Cards.AddRange(option.Cards);
                    next.Wildcards.AddRange(merged.Wildcards);
                    next.Wildcards.AddRange(option.Wildcards);
                    Walk(index + 1, nextUsed, next);
                }
            }
        }

        private static bool Overlaps(IEnumerable<Card> first, IEnumerable<Card> second)
        {
            var ids = new HashSet<string>(first.Select(card => card.Id), StringComparer.Ordinal);
            return second.Any(card => ids.Contains(card.Id));
        }

        private static IEnumerable<IReadOnlyList<T>> Combinations<T>(IReadOnlyList<T> source, int count)
        {
            if (count == 0)
            {
                yield return Array.Empty<T>();
                yield break;
            }

            if (count < 0 || count > source.Count) yield break;
            var indices = Enumerable.Range(0, count).ToArray();
            while (true)
            {
                var values = new T[count];
                for (var i = 0; i < count; i++) values[i] = source[indices[i]];
                yield return values;

                var cursor = count - 1;
                while (cursor >= 0 && indices[cursor] == source.Count - count + cursor) cursor--;
                if (cursor < 0) yield break;
                indices[cursor]++;
                for (var i = cursor + 1; i < count; i++) indices[i] = indices[i - 1] + 1;
            }
        }
    }
}
