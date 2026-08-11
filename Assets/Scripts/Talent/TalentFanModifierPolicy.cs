using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Talents
{
    public static class TalentFanModifierPolicy
    {
        public const int MinPerEffect = -4;
        public const int MinTotal = -8;

        public static int ClampPenalty(int requested) =>
            Math.Max(MinPerEffect, Math.Min(0, requested));

        public static int SumPenalties(IEnumerable<int> requested)
        {
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            return Math.Max(MinTotal, requested.Sum(ClampPenalty));
        }
    }
}
