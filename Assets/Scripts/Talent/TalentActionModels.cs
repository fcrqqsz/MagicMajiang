using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MahjongGame.Talents
{
    public static class TalentActionErrorCodes
    {
        public const string NotAvailable = "NotAvailable";
        public const string InvalidTarget = "InvalidTarget";
        public const string InsufficientResource = "InsufficientResource";
        public const string AlreadyUsedThisTurn = "AlreadyUsedThisTurn";
        public const string NotCarriedOrInactive = "NotCarriedOrInactive";
        public const string InvalidChoice = "InvalidChoice";
    }

    public enum TalentChoiceKind
    {
        Mode = 1,
        Suit = 2,
        Seat = 3,
        Tile = 4
    }

    public sealed class TalentChoiceOption
    {
        public string ChoiceId { get; }
        public string DisplayKey { get; }
        public int Value { get; }
        public TalentTileFacts Tile { get; }

        public TalentChoiceOption(
            string choiceId,
            string displayKey,
            int value = 0,
            TalentTileFacts tile = null)
        {
            if (string.IsNullOrWhiteSpace(choiceId) || choiceId.Length > 64)
                throw new ArgumentException("Choice id must contain 1..64 characters.", nameof(choiceId));
            if (string.IsNullOrWhiteSpace(displayKey) || displayKey.Length > 128)
                throw new ArgumentException("Choice display key must contain 1..128 characters.", nameof(displayKey));
            ChoiceId = choiceId;
            DisplayKey = displayKey;
            Value = value;
            Tile = tile;
        }
    }

    public sealed class TalentChoiceSet
    {
        public const int MaximumOptions = 8;
        private readonly ReadOnlyCollection<TalentChoiceOption> _options;

        public TalentChoiceKind Kind { get; }
        public string PromptKey { get; }
        public string DefaultChoiceId { get; }
        public IReadOnlyList<TalentChoiceOption> Options => _options;

        public TalentChoiceSet(
            TalentChoiceKind kind,
            string promptKey,
            string defaultChoiceId,
            IEnumerable<TalentChoiceOption> options)
        {
            if (!Enum.IsDefined(typeof(TalentChoiceKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(promptKey) || promptKey.Length > 128)
                throw new ArgumentException("Choice prompt key must contain 1..128 characters.", nameof(promptKey));
            TalentChoiceOption[] copied = (options ?? Enumerable.Empty<TalentChoiceOption>())
                .Where(option => option != null)
                .ToArray();
            if (copied.Length == 0 || copied.Length > MaximumOptions)
                throw new ArgumentOutOfRangeException(nameof(options), $"Choice sets require 1..{MaximumOptions} options.");
            if (copied.Select(option => option.ChoiceId).Distinct(StringComparer.Ordinal).Count() != copied.Length)
                throw new ArgumentException("Choice ids must be unique.", nameof(options));
            if (string.IsNullOrWhiteSpace(defaultChoiceId)
                || !copied.Any(option => string.Equals(
                    option.ChoiceId,
                    defaultChoiceId,
                    StringComparison.Ordinal)))
            {
                throw new ArgumentException("Default choice id must identify one advertised option.", nameof(defaultChoiceId));
            }
            if (kind == TalentChoiceKind.Tile && copied.Any(option => option.Tile == null))
                throw new ArgumentException("Every tile choice requires immutable tile facts.", nameof(options));
            if (kind == TalentChoiceKind.Seat && copied.Any(option => option.Value < 0 || option.Value > 3))
                throw new ArgumentException("Seat choice values must be 0..3.", nameof(options));

            Kind = kind;
            PromptKey = promptKey;
            DefaultChoiceId = defaultChoiceId;
            _options = Array.AsReadOnly(copied);
        }

        public bool Contains(string choiceId) =>
            !string.IsNullOrWhiteSpace(choiceId)
            && _options.Any(option => string.Equals(
                option.ChoiceId,
                choiceId,
                StringComparison.Ordinal));
    }

    public sealed class TalentActionOption
    {
        public string TalentId { get; set; }
        public int TargetSeatIndex { get; set; } = -1;
        public string TargetTalentId { get; set; }
        public int TargetPublicCharge { get; set; }
        public int AiPriority { get; set; }
        public TalentChoiceSet Choice { get; set; }
        public string SelectedChoiceId { get; set; }
    }

    public sealed class TalentActionRequest
    {
        public long DecisionId { get; set; }
        public string TalentId { get; set; }
        public int TargetSeatIndex { get; set; } = -1;
        public string TargetTalentId { get; set; }
        public string ChoiceId { get; set; }
    }

    public sealed class TalentActionResult
    {
        public bool Accepted { get; private set; }
        public bool EffectApplied { get; private set; }
        public string ErrorCode { get; private set; }
        public string PublicStateEventType { get; private set; }
        public int PublicStateValue { get; private set; }

        public static TalentActionResult Success(
            bool effectApplied,
            string publicStateEventType = null,
            int publicStateValue = 0) => new TalentActionResult
        {
            Accepted = true,
            EffectApplied = effectApplied,
            PublicStateEventType = publicStateEventType,
            PublicStateValue = publicStateValue
        };

        public static TalentActionResult Reject(string code) => new TalentActionResult
        {
            Accepted = false,
            ErrorCode = code
        };

        public static TalentActionResult NotSupported() => Reject(TalentActionErrorCodes.NotAvailable);
    }
}
