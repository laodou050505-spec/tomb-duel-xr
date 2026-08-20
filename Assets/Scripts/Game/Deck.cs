using System;
using System.Collections.Generic;

namespace Guandan.Game
{
    public static class Deck
    {
        public const int CardCount = 108;

        public static List<Card> CreateDoubleDeck()
        {
            var cards = new List<Card>(CardCount);
            for (var deckIndex = 0; deckIndex < 2; deckIndex++)
            {
                foreach (var suit in new[] { CardSuit.Clubs, CardSuit.Diamonds, CardSuit.Hearts, CardSuit.Spades })
                {
                    for (var rank = 2; rank <= 14; rank++)
                    {
                        cards.Add(new Card($"D{deckIndex}-{suit}-{rank}", rank, suit, deckIndex));
                    }
                }

                cards.Add(new Card($"D{deckIndex}-Joker-Small", 15, CardSuit.Joker, deckIndex));
                cards.Add(new Card($"D{deckIndex}-Joker-Big", 16, CardSuit.Joker, deckIndex));
            }

            return cards;
        }

        public static void Shuffle<T>(IList<T> items, Random random)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var swapIndex = random.Next(i + 1);
                (items[i], items[swapIndex]) = (items[swapIndex], items[i]);
            }
        }
    }
}
