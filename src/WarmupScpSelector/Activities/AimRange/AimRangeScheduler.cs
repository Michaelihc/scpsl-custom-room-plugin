using System;
using System.Collections.Generic;
using MEC;
using UnityEngine;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>The range's only coroutine. Movement uses absolute elapsed time; occupancy uses tick division.</summary>
    internal sealed class AimRangeScheduler
    {
        private CoroutineHandle _handle;
        private int _generation;

        public void Start(float tickRateHz, float occupancyRateHz, Action<double, bool> tick)
        {
            Stop();
            int generation = ++_generation;
            float rate = Sanitize(tickRateHz, 5f, 60f, 20f);
            float occupancyRate = Sanitize(occupancyRateHz, 1f, rate, 5f);
            int divider = Math.Max(1, (int)Math.Round(rate / occupancyRate));
            _handle = Timing.RunCoroutine(Run(generation, 1f / rate, divider, tick));
        }

        public void Stop()
        {
            _generation++;
            Timing.KillCoroutines(_handle);
            _handle = default;
        }

        private IEnumerator<float> Run(int generation, float interval, int occupancyDivider, Action<double, bool> tick)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            int index = 0;
            while (generation == _generation)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - started;
                tick?.Invoke(elapsed, index++ % occupancyDivider == 0);
                yield return Timing.WaitForSeconds(interval);
            }
        }

        private static float Sanitize(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
