namespace TTTXO.CloudCode
{
    /// <summary>
    /// Pure season-boundary math (design-doc.md section 6.5). Seasons are whole CALENDAR months
    /// anchored on <see cref="RankedConfigDto.SeasonStartUnixSeconds"/>: season 1 opens at that
    /// instant and every later boundary falls on the same day-of-month at the same UTC time
    /// (currently the 28th at 00:00 UTC). Numbered 1, 2, 3..., each lasting
    /// <see cref="RankedConfigDto.SeasonDurationMonths"/> months - no state/Cloud Save I/O of its own.
    ///
    /// Calendar arithmetic rather than a fixed day count on purpose: months are 28-31 days long, so
    /// NO value of "duration in days" reproduces "the 28th of every month" - it drifts backwards a
    /// day or three every month and desynchronises from the Leaderboards reset within one season.
    ///
    /// IMPORTANT: this must stay in sync with the Leaderboards <c>ResetConfig.Start</c>/<c>Schedule</c>
    /// actually deployed for the <c>ranked_3x3</c>/<c>ranked_6x6</c> leaderboards (see
    /// Assets/Leaderboards/README.md) - two independently deployed config surfaces (Remote Config's
    /// <c>RANKED_CONFIG</c> vs the Leaderboards service's own reset schedule) driven by the same
    /// anchor, kept aligned by whoever deploys. The alignment is no longer convention-only: it is
    /// asserted by <c>Assets/Scripts/Tests/Game/SeasonConfigAlignmentTests.cs</c>.
    /// </summary>
    public static class RankedSeasonCalculator
    {
        /// <summary>1-based season id "live" at <paramref name="nowUnixSeconds"/>. Season 1 covers [anchor, anchor + N months); before the anchor (e.g. a misconfigured/unset config) this clamps to season 1 rather than going to 0 or negative.</summary>
        public static int CurrentSeasonId(RankedConfigDto config, long nowUnixSeconds)
        {
            int monthsPerSeason = Math.Max(1, config.SeasonDurationMonths);
            DateTimeOffset anchor = DateTimeOffset.FromUnixTimeSeconds(config.SeasonStartUnixSeconds);
            DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(nowUnixSeconds);
            if (now < anchor)
            {
                return 1;
            }

            // Whole months between the two dates, then walk back one if this month's boundary
            // (the anchor's day-of-month) has not been crossed yet.
            int elapsedMonths = ((now.Year - anchor.Year) * 12) + now.Month - anchor.Month;
            if (anchor.AddMonths(elapsedMonths) > now)
            {
                elapsedMonths--;
            }

            return 1 + (elapsedMonths / monthsPerSeason);
        }

        /// <summary>Unix-seconds boundary at which <paramref name="seasonId"/> ends (and the next one begins) - used to know "has the season this player's cached SeasonId belongs to already closed" without re-deriving it from `now` (see RankedProfileStore's rollover check), and to feed the "quedan N dias" countdown in the UI.</summary>
        public static long SeasonEndUnixSeconds(RankedConfigDto config, int seasonId)
        {
            int monthsPerSeason = Math.Max(1, config.SeasonDurationMonths);
            DateTimeOffset anchor = DateTimeOffset.FromUnixTimeSeconds(config.SeasonStartUnixSeconds);

            // Always measured from the anchor, never by repeatedly adding a month to the previous
            // boundary: AddMonths clamps to the last valid day (Jan 31 -> Feb 28), and recomputing
            // from the anchor every time keeps that clamp from accumulating into permanent drift.
            return anchor.AddMonths(seasonId * monthsPerSeason).ToUnixTimeSeconds();
        }
    }
}
