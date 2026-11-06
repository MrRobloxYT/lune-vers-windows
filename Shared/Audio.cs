using NAudio.Wave;

namespace LuneVers;

/// Onset detector: a one-pole low-pass isolates kick and bass energy, which is
/// compared with its own slow running average. A beat is a sharp rise above
/// that average, so loud but steady music doesn't pin the lights at full.
public sealed class BeatDetector
{
    float lowState, average = 0.0005f, envelope, loudness;
    double lastBeat = -1;
    readonly float sampleRate;
    public BeatDetector(float sampleRate = 48000) { this.sampleRate = sampleRate; }

    /// Returns (energy 0…1, beat pulse 0…1).
    public (float Energy, float Beat) Process(ReadOnlySpan<float> samples, double time)
    {
        if (samples.Length == 0) return (loudness, envelope);
        float alpha = 1 - MathF.Exp(-2 * MathF.PI * 150 / sampleRate);
        float bass = 0, full = 0;
        foreach (var s in samples)
        {
            lowState += alpha * (s - lowState);
            bass += lowState * lowState;
            full += s * s;
        }
        bass /= samples.Length; full /= samples.Length;
        float duration = samples.Length / sampleRate;
        average += (bass - average) * MathF.Min(1, duration / 1.2f);
        float ratio = bass / MathF.Max(average, 1e-6f);
        float level = MathF.Min(1, MathF.Sqrt(full) * 3.2f);
        loudness += (level - loudness) * MathF.Min(1, duration / (level > loudness ? 0.05f : 0.35f));
        if (ratio > 1.35f && bass > 1e-5f && time - lastBeat > 0.18)
        {
            lastBeat = time;
            envelope = MathF.Min(1, MathF.Max(envelope, 0.55f + (ratio - 1.35f) * 0.5f));
        }
        else envelope *= MathF.Exp(-duration / 0.16f);
        return (loudness, envelope);
    }
}

/// Listens to what the PC is playing (never the microphone) and reports
/// loudness and beats. Restarts itself when the output device changes.
public sealed class AudioLevel : IDisposable
{
    WasapiLoopbackCapture? capture;
    BeatDetector detector = new();
    readonly object gate = new();
    float energy, beat;
    double stamp = -10;
    float[] mono = Array.Empty<float>();
    bool wanted;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    public bool Running => capture is not null;

    public (float Energy, float Beat, double Age) Read()
    {
        lock (gate) return (energy, beat, clock.Elapsed.TotalSeconds - stamp);
    }

    public void Start()
    {
        wanted = true;
        if (capture is not null) return;
        try
        {
            var c = new WasapiLoopbackCapture();
            var format = c.WaveFormat;
            detector = new BeatDetector(format.SampleRate);
            c.DataAvailable += (_, e) => OnData(e.Buffer, e.BytesRecorded, format);
            c.RecordingStopped += (_, _) =>
            {
                lock (gate) { capture?.Dispose(); capture = null; }
                if (wanted) Task.Delay(1500).ContinueWith(_ => Start());
            };
            capture = c;
            c.StartRecording();
        }
        catch { capture = null; }
    }

    public void Stop()
    {
        wanted = false;
        try { capture?.StopRecording(); } catch { }
        lock (gate) { energy = 0; beat = 0; }
    }

    void OnData(byte[] buffer, int bytes, WaveFormat format)
    {
        int channels = Math.Max(1, format.Channels);
        int frames;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            frames = bytes / 4 / channels;
            if (mono.Length < frames) mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++) sum += BitConverter.ToSingle(buffer, (i * channels + ch) * 4);
                mono[i] = sum / channels;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            frames = bytes / 2 / channels;
            if (mono.Length < frames) mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++) sum += BitConverter.ToInt16(buffer, (i * channels + ch) * 2) / 32768f;
                mono[i] = sum / channels;
            }
        }
        else return;
        double now = clock.Elapsed.TotalSeconds;
        var (e, b) = detector.Process(mono.AsSpan(0, frames), now);
        lock (gate) { energy = e; beat = b; stamp = now; }
    }

    public void Dispose() => Stop();
}
