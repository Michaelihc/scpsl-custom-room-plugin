namespace WarmupScpSelector.Activities
{
    /// <summary>
    /// Stable HSM hint-id building blocks for the activity suite: the lane-id constants plus each lane's three
    /// zone suffixes (hero / flash / footer). The provider prefixes these with the configured tag prefix
    /// (e.g. <c>warmupscp.</c>), so a lane's hero hint resolves to <c>warmupscp.aim.hero</c>. Keeping the IDs
    /// stable is what lets a hint be updated in place and removed explicitly (repo HSM policy). Pure string
    /// work — headless-testable and shared by both the provider and every lane.
    /// </summary>
    public static class LaneHintIds
    {
        public const string Aim = "aim";
        public const string Dodgeball = "dodgeball";
        public const string Parkour = "parkour";
        public const string Duel = "duel";

        public const string HeroZone = "hero";
        public const string FlashZone = "flash";
        public const string FooterZone = "footer";

        /// <summary>The one persistent hero line (score / clock / round score) for a lane.</summary>
        public static string Hero(string laneId) => Zone(laneId, HeroZone);

        /// <summary>The force-shown, self-clearing verdict line for a lane.</summary>
        public static string Flash(string laneId) => Zone(laneId, FlashZone);

        /// <summary>The single muted coaching line for a lane.</summary>
        public static string Footer(string laneId) => Zone(laneId, FooterZone);

        /// <summary>Compose a <c>&lt;lane&gt;.&lt;zone&gt;</c> id, tolerating blank inputs with safe defaults.</summary>
        public static string Zone(string laneId, string zone)
        {
            string lane = string.IsNullOrWhiteSpace(laneId) ? "lane" : laneId.Trim();
            string safeZone = string.IsNullOrWhiteSpace(zone) ? HeroZone : zone.Trim();
            return lane + "." + safeZone;
        }
    }
}
