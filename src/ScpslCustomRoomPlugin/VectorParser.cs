using System;
using System.Globalization;
using UnityEngine;

namespace ScpslCustomRoomPlugin
{
    internal static class VectorParser
    {
        public static Vector3 ParseVector3(string value, Vector3 fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string[] parts = value.Split(',');
            if (parts.Length != 3)
            {
                LogParseFailure(value);
                return fallback;
            }

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                LogParseFailure(value);
                return fallback;
            }

            return new Vector3(x, y, z);
        }

        public static Vector2 ParseVector2(string value, Vector2 fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string[] parts = value.Split(',');
            if (parts.Length != 2)
            {
                LogParseFailure(value);
                return fallback;
            }

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                LogParseFailure(value);
                return fallback;
            }

            return new Vector2(x, y);
        }

        private static void LogParseFailure(string value)
        {
            Exiled.API.Features.Log.Warn($"Could not parse vector config value '{value}'. Using fallback.");
        }
    }
}
