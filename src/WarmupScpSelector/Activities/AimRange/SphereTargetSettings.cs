using UnityEngine;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>
    /// Integration-facing settings for the isolated sphere-target lane. The main activity config may map its
    /// serialized values into this type without coupling the controller to a particular config file shape.
    /// </summary>
    public sealed class SphereTargetSettings
    {
        public int ActiveCount { get; set; } = 20;

        public float Diameter { get; set; } = 0.72f;

        public Color Color { get; set; } = new Color(0.20f, 0.93f, 0.85f, 1f);

        internal SphereTargetValidatedSettings Validate(int authoredPointCount)
        {
            int maximumActive = Mathf.Max(1, authoredPointCount - 1);
            return new SphereTargetValidatedSettings(
                Mathf.Clamp(ActiveCount, 1, maximumActive),
                Sanitize(Diameter, 0.2f, 1.5f, 0.72f),
                SanitizeColor(Color));
        }

        private static float Sanitize(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        private static Color SanitizeColor(Color color)
        {
            if (float.IsNaN(color.r) || float.IsInfinity(color.r) ||
                float.IsNaN(color.g) || float.IsInfinity(color.g) ||
                float.IsNaN(color.b) || float.IsInfinity(color.b) ||
                float.IsNaN(color.a) || float.IsInfinity(color.a))
            {
                return new Color(0.20f, 0.93f, 0.85f, 1f);
            }

            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);
            color.a = Mathf.Clamp(color.a, 0.15f, 1f);
            return color;
        }
    }

    internal readonly struct SphereTargetValidatedSettings
    {
        public SphereTargetValidatedSettings(
            int activeCount,
            float diameter,
            Color color)
        {
            ActiveCount = activeCount;
            Diameter = diameter;
            Color = color;
        }

        public int ActiveCount { get; }
        public float Diameter { get; }
        public Color Color { get; }
    }
}
