namespace TTTXO.CloudCode
{
    /// <summary>
    /// Pure season-close tier lookup (design-doc.md section 6.5's tier table: Bronce/Plata/Oro/
    /// Platino/Diamante). <see cref="RankedConfigDto.TierNames"/>/<see cref="RankedConfigDto.TierMinMmr"/>/
    /// <see cref="RankedConfigDto.TierSoftCurrency"/> are parallel arrays in ascending MMR order -
    /// the last entry (Diamante) has no upper bound.
    /// </summary>
    public static class RankedTierCalculator
    {
        public static string TierFor(RankedConfigDto config, int finalMmr)
        {
            int index = TierIndexFor(config, finalMmr);
            return index >= 0 && config.TierNames != null && index < config.TierNames.Length
                ? config.TierNames[index]
                : "bronze";
        }

        public static int SoftCurrencyFor(RankedConfigDto config, int finalMmr)
        {
            int index = TierIndexFor(config, finalMmr);
            return index >= 0 && config.TierSoftCurrency != null && index < config.TierSoftCurrency.Length
                ? config.TierSoftCurrency[index]
                : 0;
        }

        private static int TierIndexFor(RankedConfigDto config, int finalMmr)
        {
            if (config.TierMinMmr == null || config.TierMinMmr.Length == 0)
            {
                return -1;
            }

            int best = 0;
            for (int i = 0; i < config.TierMinMmr.Length; i++)
            {
                if (finalMmr >= config.TierMinMmr[i])
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
