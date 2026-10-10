using UnityEngine;

namespace AwesomeBalls.Cards
{
    public enum CardRarity
    {
        Common,
        Uncommon,
        Rare,
        Legendary,
    }

    /// <summary>One upgrade card (GDD 7.4). Effects are expressed as stat modifiers.</summary>
    [CreateAssetMenu(menuName = "AwesomeBalls/Card", fileName = "Card")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        public CardRarity rarity;
        [Tooltip("0 = unlimited stacks.")]
        public int maxStacks;
        public StatModifier[] modifiers;
    }
}
