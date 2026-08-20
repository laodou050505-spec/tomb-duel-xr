using System;
using System.Linq;
using Guandan.Game;
using UnityEditor;
using UnityEngine;

namespace Guandan.Editor
{
    public static class GuandanRuleSmokeTests
    {
        [MenuItem("掼蛋/验证规则烟测")]
        public static void Run()
        {
            var deck = Deck.CreateDoubleDeck();
            Require(deck.Count == 108, "双副牌应有 108 张");
            Require(deck.GroupBy(card => card.Rank).Any(group => group.Key == 2 && group.Count() == 8), "级牌数量不正确");

            var level = 2;
            var pairCards = deck.Where(card => card.Rank == 7).Take(2).ToArray();
            var pair = GuandanRuleEngine.FindPatternsForSelection(pairCards, level).FirstOrDefault(play => play.Kind == PlayKind.Pair);
            Require(pair != null, "对子识别失败");

            var triple = Find(deck.Where(card => card.Rank == 8).Take(3), level, PlayKind.Triple, "三张识别失败");
            var fullHouse = Find(
                deck.Where(card => card.Rank == 7).Take(3).Concat(deck.Where(card => card.Rank == 9).Take(2)),
                level,
                PlayKind.FullHouse,
                "三带二识别失败");
            var straight = Find(
                new[]
                {
                    deck.First(card => card.Rank == 3 && card.Suit == CardSuit.Clubs),
                    deck.First(card => card.Rank == 4 && card.Suit == CardSuit.Diamonds),
                    deck.First(card => card.Rank == 5 && card.Suit == CardSuit.Hearts),
                    deck.First(card => card.Rank == 6 && card.Suit == CardSuit.Spades),
                    deck.First(card => card.Rank == 7 && card.Suit == CardSuit.Clubs),
                },
                level,
                PlayKind.Straight,
                "顺子识别失败");
            var consecutivePairs = Find(
                new[] { 3, 4, 5 }.SelectMany(rank => deck.Where(card => card.Rank == rank).Take(2)),
                level,
                PlayKind.ConsecutivePairs,
                "三连对识别失败");
            var steelPlate = Find(
                new[] { 6, 7 }.SelectMany(rank => deck.Where(card => card.Rank == rank).Take(3)),
                level,
                PlayKind.SteelPlate,
                "钢板识别失败");
            var bomb = Find(deck.Where(card => card.Rank == 10).Take(4), level, PlayKind.Bomb, "四张炸弹识别失败");
            var straightFlush = Find(RankedSuit(deck, CardSuit.Spades, 3, 4, 5, 6, 7), level, PlayKind.StraightFlush, "同花顺识别失败");
            var jokerBomb = Find(deck.Where(card => card.IsJoker), level, PlayKind.JokerBomb, "四王炸识别失败");
            Require(GuandanRuleEngine.CanBeat(bomb, straight, level), "炸弹应能压过普通牌型");
            Require(GuandanRuleEngine.CanBeat(jokerBomb, straightFlush, level), "四王炸应能压过同花顺");
            Require(triple != null && fullHouse != null && consecutivePairs != null && steelPlate != null, "组合牌型烟测失败");

            var wildcard = deck.First(card => card.Rank == 2 && card.Suit == CardSuit.Hearts);
            var natural = deck.First(card => card.Rank == 9 && card.Suit == CardSuit.Clubs);
            var wildPair = GuandanRuleEngine.FindPatternsForSelection(new[] { wildcard, natural }, level)
                .FirstOrDefault(play => play.Kind == PlayKind.Pair);
            Require(wildPair != null && wildPair.Wildcards.Count == 1, "逢人配对子识别失败");

            var planningHand = deck.Where(card => card.Rank == 7).Take(4)
                .Concat(deck.Where(card => card.Rank == 8).Take(2))
                .Concat(deck.Where(card => card.Rank == 10).Take(1))
                .ToArray();
            var preserveBomb = Find(planningHand.Where(card => card.Rank == 8), level, PlayKind.Pair, "AI 保炸对子准备失败");
            var breakBomb = Find(planningHand.Where(card => card.Rank == 7).Take(2), level, PlayKind.Pair, "AI 拆炸对子准备失败");
            Require(
                GuandanAI.ScoreCandidate(preserveBomb, planningHand, level, 0, false)
                > GuandanAI.ScoreCandidate(breakBomb, planningHand, level, 0, false),
                "AI 应避免为普通对子拆掉天然炸弹");

            var wildcardLevel = 6;
            var levelWildcard = deck.First(card => card.Rank == wildcardLevel && card.Suit == CardSuit.Hearts);
            var nines = deck.Where(card => card.Rank == 9).Take(2).ToArray();
            var wildcardHand = new[] { levelWildcard, nines[0], nines[1], deck.First(card => card.Rank == 10) };
            var naturalNinePair = Find(nines, wildcardLevel, PlayKind.Pair, "AI 天然对子准备失败");
            var wildcardNinePair = Find(new[] { levelWildcard, nines[0] }, wildcardLevel, PlayKind.Pair, "AI 逢人配对子准备失败");
            Require(
                GuandanAI.ScoreCandidate(naturalNinePair, wildcardHand, wildcardLevel, 0, false)
                > GuandanAI.ScoreCandidate(wildcardNinePair, wildcardHand, wildcardLevel, 0, false),
                "AI 应优先保留可替代的逢人配");

            var race = new TreasureRace();
            var risky = race.Move(0, 3, TreasureRoute.Risky, 0.01);
            Require(risky.Outcome == RiskOutcome.Double && race.GetPosition(0) == 6, "激进路线翻倍结果错误");
            var steady = race.Move(1, 2, TreasureRoute.Steady, 0.5);
            Require(steady.Position == 2, "稳当路线结果错误");
            var boundaryRace = new TreasureRace();
            Require(boundaryRace.Move(0, 3, TreasureRoute.Risky, 0.10).Outcome == RiskOutcome.PlusOne, "激进 +1 边界错误");
            boundaryRace.Reset();
            Require(boundaryRace.Move(0, 3, TreasureRoute.Risky, 0.25).Outcome == RiskOutcome.Normal, "激进正常边界错误");
            boundaryRace.Reset();
            Require(boundaryRace.Move(0, 3, TreasureRoute.Risky, 0.80).Outcome == RiskOutcome.MinusOne, "激进 -1 边界错误");
            boundaryRace.Reset();
            Require(boundaryRace.Move(0, 3, TreasureRoute.Risky, 0.92).Outcome == RiskOutcome.Retreat, "激进倒退边界错误");

            var match = new GuandanMatchEngine(20260817);
            match.StartMatch();
            Require(match.Phase == MatchPhase.Dealing, "牌局必须等待发牌演出结束");
            match.BeginAfterDealPresentation();
            var actions = 0;
            while (match.Phase == MatchPhase.Playing && actions++ < 1000)
            {
                var seat = match.ActiveSeat;
                var legal = match.GetLegalPlays(seat);
                if (legal.Count > 0) match.Play(seat, legal[0]);
                else match.Pass(seat);
            }
            Require(match.Phase == MatchPhase.HandComplete, "完整小局未能结束");
            Require(match.LastHandResult?.Placements.Count == 4, "小局应确认四个名次");
            match.StartNextHand();
            match.BeginAfterDealPresentation();
            while (match.Phase == MatchPhase.TributePayment && actions++ < 1100)
            {
                var seat = match.ActiveSeat;
                var card = match.GetLegalTributeCards(seat).FirstOrDefault();
                Require(card != null && match.PayTribute(seat, card), "自动进贡失败");
            }
            if (match.Phase == MatchPhase.TributeAssignment) Require(match.AssignEqualTributes(), "同点进贡分配失败");
            while (match.Phase == MatchPhase.TributeReturn && actions++ < 1200)
            {
                var seat = match.ActiveSeat;
                var card = match.GetLegalReturnCards(seat).FirstOrDefault();
                Require(card != null && match.ReturnTribute(seat, card), "自动还贡失败");
            }
            Require(match.Phase == MatchPhase.Playing, "进还贡或抗贡后应进入正式出牌");
            Debug.Log("[Guandan] 规则烟测通过：双副牌、全部主要牌型、逢人配、完整小局、进还贡/抗贡与夺宝概率。\n");
        }

        private static PlayPattern Find(System.Collections.Generic.IEnumerable<Card> cards, int level, PlayKind kind, string message)
        {
            var pattern = GuandanRuleEngine.FindPatternsForSelection(cards.ToArray(), level)
                .FirstOrDefault(play => play.Kind == kind);
            Require(pattern != null, message);
            return pattern;
        }

        private static Card[] RankedSuit(System.Collections.Generic.IReadOnlyList<Card> deck, CardSuit suit, params int[] ranks)
        {
            return ranks.Select(rank => deck.First(card => card.Rank == rank && card.Suit == suit)).ToArray();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
