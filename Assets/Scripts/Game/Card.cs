using System;

namespace Guandan.Game
{
    public enum CardSuit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades,
        Joker,
    }

    [Serializable]
    public sealed class Card : IEquatable<Card>
    {
        public string Id { get; }
        public int Rank { get; }
        public CardSuit Suit { get; }
        public int DeckIndex { get; }

        public bool IsJoker => Suit == CardSuit.Joker;
        public bool IsSmallJoker => IsJoker && Rank == 15;
        public bool IsBigJoker => IsJoker && Rank == 16;
        public bool IsRed => Suit == CardSuit.Diamonds || Suit == CardSuit.Hearts || IsBigJoker;

        public Card(string id, int rank, CardSuit suit, int deckIndex)
        {
            Id = id;
            Rank = rank;
            Suit = suit;
            DeckIndex = deckIndex;
        }

        public bool IsWildcard(int levelRank)
        {
            return !IsJoker && Suit == CardSuit.Hearts && Rank == levelRank;
        }

        public int Power(int levelRank)
        {
            if (IsBigJoker) return 17;
            if (IsSmallJoker) return 16;
            return Rank == levelRank ? 15 : Rank;
        }

        public string RankLabel
        {
            get
            {
                if (IsSmallJoker) return "小王";
                if (IsBigJoker) return "大王";
                return Rank switch
                {
                    11 => "J",
                    12 => "Q",
                    13 => "K",
                    14 => "A",
                    _ => Rank.ToString(),
                };
            }
        }

        public string SuitLabel => Suit switch
        {
            CardSuit.Clubs => "梅",
            CardSuit.Diamonds => "方",
            CardSuit.Hearts => "红",
            CardSuit.Spades => "黑",
            _ => string.Empty,
        };

        public string ShortLabel => IsJoker ? RankLabel : $"{SuitLabel}{RankLabel}";

        public bool Equals(Card other)
        {
            return other != null && Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is Card other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            return ShortLabel;
        }
    }
}
