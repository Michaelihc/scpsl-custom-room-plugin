using System;
using System.Collections.Generic;
using System.Globalization;

namespace WarmupScpSelector.Services
{
    /// <summary>
    /// Pure change-skip cache for the provider's persistent (non-flash) HSM hints. It stores the last Y+text
    /// signature pushed for each (player, normalized hint id) key so an identical resubmission can be skipped
    /// before it ever reaches HintServiceMeow or the network. The flash zone deliberately never consults this
    /// cache (an identical repeat verdict must genuinely reappear), and removing/disabling a hint clears its
    /// entry so a later show is re-sent. No Unity/LabAPI types, so it is headless-testable.
    /// </summary>
    public sealed class HintChangeCache
    {
        private readonly Dictionary<string, string> _signatures = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count => _signatures.Count;

        /// <summary>
        /// The X+Y+text signature two submissions are compared on. X and Y are both part of the signature, so a
        /// hint that moves horizontally (e.g. the warmup status collapsing into the left Aim lane) or vertically
        /// re-sends even when its text is unchanged.
        /// </summary>
        public static string Signature(float x, float y, string text)
        {
            float safeX = float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;
            float safeY = float.IsNaN(y) || float.IsInfinity(y) ? 0f : y;
            return safeX.ToString(CultureInfo.InvariantCulture) + "|"
                + safeY.ToString(CultureInfo.InvariantCulture) + "|"
                + (text ?? string.Empty);
        }

        /// <summary>True when <paramref name="signature"/> matches the last one stored for <paramref name="key"/> (i.e. skip the push).</summary>
        public bool Matches(string key, string signature)
        {
            return key != null && _signatures.TryGetValue(key, out string previous) && previous == signature;
        }

        /// <summary>Record the signature actually pushed for <paramref name="key"/>.</summary>
        public void Set(string key, string signature)
        {
            if (key != null)
            {
                _signatures[key] = signature ?? string.Empty;
            }
        }

        /// <summary>Forget one key so its next show is re-sent (called when the hint is removed).</summary>
        public void Remove(string key)
        {
            if (key != null)
            {
                _signatures.Remove(key);
            }
        }

        /// <summary>Forget every entry (called on provider disable).</summary>
        public void Clear()
        {
            _signatures.Clear();
        }
    }
}
