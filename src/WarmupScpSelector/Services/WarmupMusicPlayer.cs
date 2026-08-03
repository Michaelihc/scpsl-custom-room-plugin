using System;
using System.Collections.Generic;
using System.IO;
using LabApi.Features.Audio;
using LabApi.Features.Wrappers;
using LabApi.Loader;
using MEC;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Services;

internal sealed class WarmupMusicPlayer
{
    private const string LogPrefix = "[WarmupScpSelector:Music]";
    private const int BytesPerSample = sizeof(float);

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly Dictionary<string, PlayerStream> _streams = new();

    private SpeakerToy? _speaker;
    private float[]? _samples;
    private float[]? _fadeInSamples;
    private CoroutineHandle _stopAfterFade;
    private byte _controllerId;
    private bool _active;
    private bool _fadingOut;

    public WarmupMusicPlayer(WarmupScpSelectorPlugin plugin)
    {
        _plugin = plugin;
    }

    private Config Config => _plugin.Config;

    public void Start(Vector3 speakerPosition)
    {
        StopImmediate();

        if (!Config.MusicEnabled || string.IsNullOrWhiteSpace(Config.MusicFilePath))
        {
            return;
        }

        try
        {
            string path = ResolvePath(Config.MusicFilePath);
            _samples = LoadF32Le(path, Math.Max(1, Config.MusicMaxSeconds));
            _fadeInSamples = BuildFadeInTrack(_samples, SecondsToSamples(Config.MusicFadeInSeconds));

            _controllerId = (byte)Clamp(Config.MusicControllerId, 1, 254);
            _speaker = SpeakerToy.Create(speakerPosition + Vector3.up * 2.5f);
            _speaker.ControllerId = _controllerId;
            _speaker.IsSpatial = Config.MusicSpatial;
            _speaker.Volume = Sanitize(Config.MusicVolume, 0f, 2f, 0.35f);
            _speaker.MinDistance = Sanitize(Config.MusicMinDistance, 0f, 100f, 2f);
            _speaker.MaxDistance = Math.Max(_speaker.MinDistance, Sanitize(Config.MusicMaxDistance, 1f, 250f, 35f));

            _active = true;
            _fadingOut = false;
            Logger.Info($"{LogPrefix} Loaded {Path.GetFileName(path)} ({_samples.Length / AudioTransmitter.SampleRate}s).");
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Music disabled: {ex.Message}");
            StopImmediate();
        }
    }

    public void AddPlayer(Player player)
    {
        if (!_active || _fadingOut || _samples == null || _speaker == null || !IsPlayable(player))
        {
            return;
        }

        string key = Key(player);
        if (_streams.ContainsKey(key))
        {
            return;
        }

        AudioTransmitter? transmitter = null;
        try
        {
            ReferenceHub hub = player.ReferenceHub;
            string userId = player.UserId ?? string.Empty;
            transmitter = new AudioTransmitter(_controllerId)
            {
                ValidPlayers = candidate => _active && !_fadingOut && IsSamePlayer(candidate, hub, userId),
            };

            transmitter.Play(_fadeInSamples ?? _samples, queue: false, loop: false);
            transmitter.Play(_samples, queue: true, loop: true);
            _streams[key] = new PlayerStream(hub, userId, transmitter);
        }
        catch (Exception ex)
        {
            try
            {
                transmitter?.Stop();
            }
            catch
            {
                // Best-effort cleanup for optional music; player flow must continue.
            }

            _streams.Remove(key);
            Logger.Warn($"{LogPrefix} Could not start music for a player: {ex.Message}");
        }
    }

    public void RemovePlayer(Player player)
    {
        if (player == null)
        {
            return;
        }

        RemovePlayer(Key(player));
    }

    public void UpdateCountdown(short nativeTimer)
    {
        if (!_active || _fadingOut || nativeTimer <= 0)
        {
            return;
        }

        float fadeBeforeStart = Sanitize(Config.MusicFadeOutBeforeStartSeconds, 0f, 60f, 5f);
        if (fadeBeforeStart > 0f && nativeTimer <= fadeBeforeStart)
        {
            FadeOutAndStop();
        }
    }

    public void FadeOutAndStop()
    {
        if (!_active || _fadingOut)
        {
            return;
        }

        _fadingOut = true;
        Timing.KillCoroutines(_stopAfterFade);

        if (_samples == null || _streams.Count == 0)
        {
            StopImmediate();
            return;
        }

        float fadeSeconds = Sanitize(Config.MusicFadeOutSeconds, 0f, 30f, 5f);
        int fadeSamples = SecondsToSamples(fadeSeconds);
        if (fadeSamples <= 0)
        {
            StopImmediate();
            return;
        }

        foreach (PlayerStream stream in _streams.Values)
        {
            try
            {
                int start = Wrap(stream.Transmitter.CurrentPosition, _samples.Length);
                float[] fade = BuildFadeOutSegment(_samples, start, fadeSamples);
                stream.Transmitter.Stop();
                stream.Transmitter.ValidPlayers = candidate => IsSamePlayer(candidate, stream.Hub, stream.UserId);
                stream.Transmitter.Play(fade, queue: false, loop: false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"{LogPrefix} Could not start fade-out for a player: {ex.Message}");
            }
        }

        _stopAfterFade = Timing.CallDelayed(fadeSeconds + 0.25f, StopImmediate);
    }

    public void StopImmediate()
    {
        Timing.KillCoroutines(_stopAfterFade);

        foreach (PlayerStream stream in _streams.Values)
        {
            try
            {
                stream.Transmitter.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn($"{LogPrefix} Could not stop a music stream: {ex.Message}");
            }
        }

        _streams.Clear();

        try
        {
            if (_speaker != null && !_speaker.IsDestroyed)
            {
                _speaker.Destroy();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Could not destroy speaker: {ex.Message}");
        }

        _speaker = null;
        _samples = null;
        _fadeInSamples = null;
        _active = false;
        _fadingOut = false;
    }

    private void RemovePlayer(string key)
    {
        if (!_streams.TryGetValue(key, out PlayerStream stream))
        {
            return;
        }

        try
        {
            stream.Transmitter.Stop();
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Could not stop a leaving player's stream: {ex.Message}");
        }

        _streams.Remove(key);
    }

    private string ResolvePath(string configuredPath)
    {
        string path = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(_plugin.GetConfigDirectory().FullName, path);
    }

    private static float[] LoadF32Le(string path, int maxSeconds)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"file not found: {path}");
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length % BytesPerSample != 0)
        {
            throw new InvalidDataException("expected non-empty raw float32 PCM bytes with a length divisible by 4");
        }

        int maxBytes = checked(AudioTransmitter.SampleRate * maxSeconds * BytesPerSample);
        if (bytes.Length > maxBytes)
        {
            throw new InvalidDataException($"music is longer than MusicMaxSeconds ({maxSeconds}s)");
        }

        float[] samples = new float[bytes.Length / BytesPerSample];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = samples[i];
            samples[i] = float.IsNaN(sample) || float.IsInfinity(sample) ? 0f : Mathf.Clamp(sample, -1f, 1f);
        }

        return samples;
    }

    private static float[] BuildFadeInTrack(float[] samples, int fadeSamples)
    {
        if (fadeSamples <= 0)
        {
            return samples;
        }

        float[] faded = new float[samples.Length];
        int limit = Math.Min(fadeSamples, faded.Length);
        for (int i = 0; i < faded.Length; i++)
        {
            float gain = i < limit ? i / (float)limit : 1f;
            faded[i] = samples[i] * gain;
        }

        return faded;
    }

    private static float[] BuildFadeOutSegment(float[] samples, int start, int length)
    {
        float[] faded = new float[length];
        for (int i = 0; i < faded.Length; i++)
        {
            int sourceIndex = (start + i) % samples.Length;
            float gain = 1f - (i / (float)faded.Length);
            faded[i] = samples[sourceIndex] * gain;
        }

        return faded;
    }

    private static int SecondsToSamples(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
        {
            return 0;
        }

        return Mathf.RoundToInt(seconds * AudioTransmitter.SampleRate);
    }

    private static bool IsPlayable(Player player)
    {
        return player?.ReferenceHub != null && (player.IsDummy || (player.IsPlayer && player.IsReady));
    }

    private static bool IsSamePlayer(Player candidate, ReferenceHub hub, string userId)
    {
        return candidate?.ReferenceHub != null &&
            (candidate.ReferenceHub == hub ||
             (!string.IsNullOrWhiteSpace(userId) && string.Equals(candidate.UserId, userId, StringComparison.Ordinal)));
    }

    private static string Key(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player.UserId))
        {
            return player.UserId;
        }

        return player.ReferenceHub != null ? player.ReferenceHub.GetInstanceID().ToString() : player.GetHashCode().ToString();
    }

    private static int Wrap(int value, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        int wrapped = value % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static float Sanitize(float value, float min, float max, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    private readonly struct PlayerStream
    {
        public PlayerStream(ReferenceHub hub, string userId, AudioTransmitter transmitter)
        {
            Hub = hub;
            UserId = userId;
            Transmitter = transmitter;
        }

        public ReferenceHub Hub { get; }

        public string UserId { get; }

        public AudioTransmitter Transmitter { get; }
    }
}
