using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AwesomeBalls.Cards
{
    /// <summary>
    /// All cards in the game; a card's network id is its index here. Generates card offers with
    /// rarity weights and stack limits (GDD 7.1).
    /// </summary>
    [CreateAssetMenu(menuName = "AwesomeBalls/Card Database", fileName = "CardDatabase")]
    public sealed class CardDatabase : ScriptableObject
    {
        public CardDefinition[] cards;

        static readonly float[] RarityWeights = { 55f, 30f, 12f, 3f };

        public int Count => cards.Length;

        public CardDefinition Get(int id) => id >= 0 && id < cards.Length ? cards[id] : null;

        public StatBlock BuildStats(IEnumerable<int> cardIds)
        {
            var block = new StatBlock();
            foreach (int id in cardIds)
            {
                CardDefinition card = Get(id);
                if (card == null) continue;
                foreach (StatModifier m in card.modifiers) block.Apply(m);
            }
            return block;
        }

        /// <summary>
        /// Up to <paramref name="count"/> distinct cards the player can still take, weighted by rarity,
        /// with at least one Uncommon or better when possible (more if <paramref name="minRarity"/> asks).
        /// </summary>
        public List<int> GenerateOffer(System.Random rng, IReadOnlyList<int> owned, int count, CardRarity minGuaranteed = CardRarity.Uncommon)
        {
            var available = new List<int>();
            for (int id = 0; id < cards.Length; id++)
            {
                int stacks = owned.Count(o => o == id);
                if (cards[id].maxStacks == 0 || stacks < cards[id].maxStacks) available.Add(id);
            }

            var offer = new List<int>();
            List<int> guaranteed = available.Where(id => cards[id].rarity >= minGuaranteed).ToList();
            if (guaranteed.Count > 0)
            {
                int pick = PickWeighted(rng, guaranteed);
                offer.Add(pick);
                available.Remove(pick);
            }

            while (offer.Count < count && available.Count > 0)
            {
                int pick = PickWeighted(rng, available);
                offer.Add(pick);
                available.Remove(pick);
            }

            // Shuffle so the guaranteed card isn't always first.
            for (int i = offer.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (offer[i], offer[j]) = (offer[j], offer[i]);
            }
            return offer;
        }

        int PickWeighted(System.Random rng, List<int> ids)
        {
            float total = ids.Sum(id => RarityWeights[(int)cards[id].rarity]);
            float roll = (float)rng.NextDouble() * total;
            foreach (int id in ids)
            {
                roll -= RarityWeights[(int)cards[id].rarity];
                if (roll <= 0f) return id;
            }
            return ids[ids.Count - 1];
        }
    }
}
