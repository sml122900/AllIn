using System;
using AllIn.Core;

namespace AllIn.Holdem
{
    /// <summary>A card as 0..51: rank index = c / 4 (0 = deuce … 12 = ace), suit = c % 4 (s, h, d, c).</summary>
    public static class Card
    {
        public const string Ranks = "23456789TJQKA";
        public const string Suits = "shdc";
        public static readonly string[] SuitSymbols = { "♠", "♥", "♦", "♣" };

        public static int Make(int rankIndex, int suit) => rankIndex * 4 + suit;
        public static int Rank(int card) => card >> 2;          // 0..12
        public static int Suit(int card) => card & 3;           // 0..3
        public static bool IsRed(int card) => Suit(card) == 1 || Suit(card) == 2;

        /// <summary>"As" → ace of spades. Case-insensitive rank, suit one of s/h/d/c.</summary>
        public static int Parse(string s)
        {
            if (s == null || s.Length != 2) throw new FormatException("card: " + s);
            int r = Ranks.IndexOf(char.ToUpperInvariant(s[0]));
            int u = Suits.IndexOf(char.ToLowerInvariant(s[1]));
            if (r < 0 || u < 0) throw new FormatException("card: " + s);
            return Make(r, u);
        }

        /// <summary>"As Kd 2c" → cards.</summary>
        public static int[] ParseMany(string s)
        {
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cards = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) cards[i] = Parse(parts[i]);
            return cards;
        }

        public static string ToText(int card) => Ranks[Rank(card)].ToString() + Suits[Suit(card)];

        public static string RankText(int card) => Rank(card) == 8 ? "10" : Ranks[Rank(card)].ToString();
    }

    /// <summary>52 cards, shuffled with the injected RNG; deal from the top.</summary>
    public sealed class Deck
    {
        readonly int[] _cards = new int[52];
        int _next;

        public Deck(IRng rng)
        {
            for (int i = 0; i < 52; i++) _cards[i] = i;
            rng.Shuffle(_cards);
        }

        public int Deal() => _cards[_next++];

        public int Remaining => 52 - _next;

        // ---- test rigging (internal: visible to AllIn.Holdem.Tests only) ----

        int IndexOf(int card) => Array.IndexOf(_cards, card);

        internal void Swap(int a, int b)
        {
            int i = IndexOf(a), j = IndexOf(b);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }

        /// <summary>Makes these the next cards dealt.</summary>
        internal void PlaceNext(int[] cards)
        {
            for (int k = 0; k < cards.Length; k++)
            {
                int j = IndexOf(cards[k]);
                if (j < _next) throw new InvalidOperationException(Card.ToText(cards[k]) + " already dealt");
                (_cards[_next + k], _cards[j]) = (_cards[j], _cards[_next + k]);
            }
        }
    }
}
