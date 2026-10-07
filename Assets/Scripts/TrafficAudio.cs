using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Traffic and city sound.
///
/// Every car gets two 3D sounds as it appears (like the night-mode headlights): an idling engine and a driving sound
/// (engine + tyres), cross-faded by speed. Stopped cars (at lights, yielding, or during the midpoint pause) idle; moving
/// cars get the driving sound, louder and higher-pitched as they speed up. Sounds fade with distance like real sound
/// (about -6 dB per doubling of distance) and shift pitch as cars pass (Doppler). Each car is given one of the idle and
/// one of the driving recordings, fixed by its vehicle number, so the same run always sounds the same.
///
/// The recordings are prepared once at start-up, without changing the files: any quiet fade-in/fade-out is trimmed off,
/// the end is cross-faded into the start so the loop has no click or dip, and each recording is brought to a common
/// loudness so different files mix evenly. The city ambience on <see cref="ambienceSource"/> is prepared the same way.
/// If no driving recordings are assigned, a generated placeholder sound is used.
/// </summary>
public class TrafficAudio : MonoBehaviour
{
    [Header("Car recordings")]
    [Tooltip("Looping recordings of a car driving at a steady speed (engine + tyres). Each car uses one of them.")]
    public AudioClip[] drivingLoops = new AudioClip[0];
    [Tooltip("Looping recordings of a stationary car with its engine idling. Each car uses one of them.")]
    public AudioClip[] idleLoops = new AudioClip[0];

    [Header("Loudness and pitch by speed")]
    [Tooltip("Speed at which the driving sound is at full volume and pitch (m/s).")]
    [Min(0.1f)] public float referenceSpeed = 14f;
    [Tooltip("Speed by which the idle sound has fully faded into the driving sound (m/s).")]
    [Min(0.1f)] public float idleToDrivingSpeed = 4f;
    [Range(0f, 1f)] public float idleVolume = 0.35f;
    [Range(0f, 1f)] public float drivingVolumeSlow = 0.35f;
    [Range(0f, 1f)] public float drivingVolumeFull = 0.9f;
    [Min(0.1f)] public float drivingPitchSlow = 0.8f;
    [Min(0.1f)] public float drivingPitchFull = 1.15f;

    [Header("3D sound")]
    [Tooltip("Within this distance a car is at full volume; beyond it the sound fades about 6 dB per doubling of distance (m).")]
    [Min(0.1f)] public float minDistance = 5f;
    [Tooltip("Beyond this distance a car is silent (m).")]
    [Min(1f)] public float maxDistance = 150f;
    [Tooltip("Pitch shift as cars approach and pass (1 = physically realistic).")]
    [Range(0f, 5f)] public float dopplerLevel = 1f;

    [Header("City ambience")]
    [Tooltip("The scene's non-3D background ambience source. Found by name (\"Background Noise\") if empty.")]
    public AudioSource ambienceSource;
    [Tooltip("Looping city ambience recording. Empty = keep the clip already on the ambience source.")]
    public AudioClip ambienceLoop;
    [Tooltip("About 15-18 dB below a nearby passing car, as on a real street.")]
    [Range(0f, 1f)] public float ambienceVolume = 0.5f;

    // Common loudness (RMS, dBFS) the prepared recordings are brought to
    const float DrivingLevelDb = -16f, IdleLevelDb = -20f, AmbienceLevelDb = -30f;

    AudioClip[] mDriving, mIdle;
    readonly Dictionary<CarMovement, (AudioSource idle, AudioSource driving)> mSources = new Dictionary<CarMovement, (AudioSource, AudioSource)>();
    readonly List<CarMovement> mGone = new List<CarMovement>();

    void Awake()
    {
        mDriving = PrepareAll(drivingLoops, DrivingLevelDb);
        if (mDriving.Length == 0)
            mDriving = new[] { CreatePlaceholderClip() };
        mIdle = PrepareAll(idleLoops, IdleLevelDb);

        if (ambienceSource == null)
        {
            var go = GameObject.Find("Background Noise");
            if (go != null)
                ambienceSource = go.GetComponent<AudioSource>();
        }
        if (ambienceSource != null)
        {
            if (ambienceLoop != null)
            {
                ambienceSource.clip = PrepareLoop(ambienceLoop, AmbienceLevelDb, mono: false);
                ambienceSource.loop = true;
                ambienceSource.Play();
            }
            ambienceSource.volume = ambienceVolume;
        }
    }

    void Update()
    {
        foreach (CarMovement car in CarMovement.ActiveVehicles)
        {
            if (car == null)
                continue;
            if (!mSources.TryGetValue(car, out var s) || s.driving == null)
                mSources[car] = s = AddSources(car);

            float drive = Mathf.SmoothStep(0f, 1f, car.CurrentSpeed / idleToDrivingSpeed); // 0 = idling, 1 = driving
            float speed01 = Mathf.Clamp01(car.CurrentSpeed / referenceSpeed);
            SetLevel(s.driving, drive * Mathf.Lerp(drivingVolumeSlow, drivingVolumeFull, speed01));
            s.driving.pitch = Mathf.Lerp(drivingPitchSlow, drivingPitchFull, speed01);
            if (s.idle != null)
                SetLevel(s.idle, (1f - drive) * idleVolume);
        }

        // Forget cars that have left the road
        mGone.Clear();
        foreach (var pair in mSources)
            if (pair.Key == null)
                mGone.Add(pair.Key);
        foreach (CarMovement car in mGone)
            mSources.Remove(car);
    }

    // A silent sound is paused, so it does not take one of the limited playing voices
    static void SetLevel(AudioSource source, float volume)
    {
        source.volume = volume;
        if (volume < 0.005f) { if (source.isPlaying) source.Pause(); }
        else if (!source.isPlaying) source.UnPause();
    }

    (AudioSource idle, AudioSource driving) AddSources(CarMovement car)
    {
        int id = Mathf.Max(0, car.VehicleId);
        AudioSource driving = AddSource(car, "DrivingAudio", mDriving[id % mDriving.Length], id);
        AudioSource idle = mIdle.Length > 0 ? AddSource(car, "IdleAudio", mIdle[(id / mDriving.Length) % mIdle.Length], id) : null;
        return (idle, driving);
    }

    AudioSource AddSource(CarMovement car, string name, AudioClip clip, int id)
    {
        var go = new GameObject(name);
        go.transform.SetParent(car.transform, false);
        go.transform.position = car.transform.position + Vector3.up * 0.5f;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 1f;                          // fully 3D
        source.rolloffMode = AudioRolloffMode.Logarithmic; // realistic inverse-distance fade
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = dopplerLevel;
        source.playOnAwake = false;
        source.volume = 0f;
        // Start each car at a different point in the loop (fixed per car), so cars don't sound in sync
        source.time = (id * 0.37f) % Mathf.Max(0.01f, clip.length);
        source.Play();
        return source;
    }

    static AudioClip[] PrepareAll(AudioClip[] clips, float levelDb)
    {
        var list = new List<AudioClip>();
        if (clips != null)
            foreach (AudioClip c in clips)
                if (c != null)
                    list.Add(PrepareLoop(c, levelDb, mono: true));
        return list.ToArray();
    }

    /// <summary>
    /// Makes a seamless, level-matched loop from a recording: trims quiet fade-in/fade-out sections (more than 3 dB below the
    /// recording's typical level), cross-fades the last 0.25 s into the start, and scales it to <paramref name="levelDb"/>
    /// RMS (without letting peaks clip). Car sounds are mixed down to mono for 3D playback. The original clip is unchanged;
    /// if its samples can't be read, it is used as it is.
    /// </summary>
    static AudioClip PrepareLoop(AudioClip clip, float levelDb, bool mono)
    {
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();
        int ch = clip.channels, rate = clip.frequency;
        var raw = new float[clip.samples * ch];
        if (!clip.GetData(raw, 0))
            return clip;

        int outCh = mono ? 1 : ch;
        int frames = clip.samples;
        var samples = new float[frames * outCh];
        for (int f = 0; f < frames; f++)
        {
            if (mono)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++) sum += raw[f * ch + c];
                samples[f] = sum / ch;
            }
            else
            {
                for (int c = 0; c < ch; c++) samples[f * outCh + c] = raw[f * ch + c];
            }
        }

        // Loudness in 0.25 s windows; keep the span from the first to the last window within 3 dB of the median
        // (trims fade-ins/outs and the louder/quieter ends of a pass-by, so the loop doesn't swell or dip)
        int win = Mathf.Max(1, rate / 4);
        int windows = frames / win;
        var level = new float[windows];
        for (int w = 0; w < windows; w++)
        {
            double sum = 0;
            for (int i = w * win * outCh; i < (w + 1) * win * outCh; i++) sum += samples[i] * samples[i];
            level[w] = 10f * Mathf.Log10((float)(sum / (win * outCh)) + 1e-12f);
        }
        var sorted = (float[])level.Clone();
        System.Array.Sort(sorted);
        float floor = windows > 0 ? sorted[windows / 2] - 3f : -999f;
        int first = 0, last = windows - 1;
        while (first < last && level[first] < floor) first++;
        while (last > first && level[last] < floor) last--;
        int start = first * win, end = Mathf.Min(frames, (last + 1) * win);

        // Seamless loop: the 0.25 s after the loop end is cross-faded into the loop start
        int fade = Mathf.Min(rate / 4, (end - start) / 4);
        int length = end - start - fade;
        if (length <= 0)
            return clip;
        var loop = new float[length * outCh];
        double energy = 0; float peak = 0f;
        for (int f = 0; f < length; f++)
            for (int c = 0; c < outCh; c++)
            {
                float v = samples[(start + f) * outCh + c];
                if (f < fade)
                    v = Mathf.Lerp(samples[(start + length + f) * outCh + c], v, (float)f / fade);
                loop[f * outCh + c] = v;
                energy += v * v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }

        // Common loudness, limited so the loudest peak stays below full scale
        float rms = Mathf.Sqrt((float)(energy / loop.Length));
        float gain = Mathf.Pow(10f, levelDb / 20f) / Mathf.Max(rms, 1e-6f);
        gain = Mathf.Min(gain, 0.95f / Mathf.Max(peak, 1e-6f));
        for (int i = 0; i < loop.Length; i++) loop[i] *= gain;

        var result = AudioClip.Create(clip.name + " (loop)", length, outCh, rate, false);
        result.SetData(loop, 0);
        return result;
    }

    /// <summary>Fallback when no recordings are assigned: 4 s of band-limited road noise plus a low engine hum (fixed seed).</summary>
    static AudioClip CreatePlaceholderClip()
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int length = rate * 4, fade = rate / 4;
        var raw = new float[length + fade];
        var rng = new System.Random(12345);
        float low = 0f, high = 0f, prev = 0f;
        float lowCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * 1200f / rate);
        float highCoeff = Mathf.Exp(-2f * Mathf.PI * 150f / rate);
        for (int i = 0; i < raw.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += lowCoeff * (white - low);
            high = highCoeff * (high + low - prev);
            prev = low;
            float t = (float)i / rate;
            float wobble = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.7f * t);
            float hum = wobble * (0.5f * Mathf.Sin(2f * Mathf.PI * 35f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 70f * t)
                                  + 0.15f * Mathf.Sin(2f * Mathf.PI * 105f * t));
            raw[i] = 1.6f * high + 0.35f * hum;
        }
        var data = new float[length];
        float peak = 0f;
        for (int i = 0; i < length; i++)
        {
            data[i] = i < fade ? Mathf.Lerp(raw[length + i], raw[i], (float)i / fade) : raw[i];
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        for (int i = 0; i < length; i++)
            data[i] *= 0.8f / Mathf.Max(peak, 1e-4f);
        var clip = AudioClip.Create("VehicleLoop (generated)", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
