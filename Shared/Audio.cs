using NAudio.Wave;

namespace LuneVers;

/// Onset detector: a one-pole low-pass isolates kick and bass energy, which is
/// compared with its own slow running average. A beat is a sharp rise above
/// that average, so loud but steady music doesn't pin the lights at full.
/// It also tracks the song's tempo (where the beats fall) and its sections
/// (louder or quieter than the last ~20 s), the same way as the Mac app.
public sealed class BeatDetector
{
    float lowState, average = 0.0005f, envelope, loudness;
    double lastBeat = -1;
    readonly float sampleRate;
    public BeatDetector(float sampleRate = 48000) { this.sampleRate = sampleRate; }

    // Tempo tracking: spectral flux forms an onset track, 10 ms per step,
    // over the last 8 s. Its repeating gap is the beat; a comb finds where
    // the beats fall.
    readonly float[] onsets = new float[800];
    int onsetHead, onsetCount, steps;
    float stepSeconds = 0.01f;
    /// Seconds per beat (0 = not sure yet).
    public float Period { get; private set; }
    /// 0…1.
    public float Confidence { get; private set; }
    /// When the last beat fell, on the clock passed to Process.
    public double BeatTime { get; private set; }
    /// How much louder (dB / 6) the last ~1.5 s is than the last ~20 s: +1 is a drop, below 0 a quiet part.
    public float Section { get; private set; }
    float shortDB = -100, longDB = -100, heard;

    static readonly float[] Window = MakeWindow();
    static float[] MakeWindow()
    {
        // Same as vDSP's normalized Hann window on the Mac.
        var w = new float[1024];
        for (int i = 0; i < 1024; i++) w[i] = 0.8164966f * (1 - MathF.Cos(2 * MathF.PI * i / 1024));
        return w;
    }
    readonly float[] ring = new float[1024];
    int ringHead;
    readonly float[] lastSpectrum = new float[256];
    readonly float[] re = new float[1024], im = new float[1024];
    Dictionary<int, float> votes = new();

    /// In-place radix-2 FFT of 1024 points.
    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    float t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
    }

    // Spectral flux: how much each frequency got louder since the last hop,
    // summed, bass counted double. Every kick, snare, bass note or chord is a spike.
    float Flux(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) { ring[ringHead] = s; ringHead = (ringHead + 1) & 1023; }
        for (int i = 0; i < 1024; i++) { re[i] = ring[(ringHead + i) & 1023] * Window[i]; im[i] = 0; }
        Fft(re, im);
        float total = 0;
        int top = Math.Min(255, (int)(9000 / (sampleRate / 1024)));   // up to ~9 kHz
        for (int k = 1; k <= top; k++)
        {
            // x2: vDSP's FFT on the Mac is scaled by 2.
            float mag = 2 * MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);
            float level = MathF.Log(1 + 100 * mag);
            float rise = level - lastSpectrum[k];
            lastSpectrum[k] = level;
            if (rise > 0) total += rise * (k <= 4 ? 2 : 1);
        }
        return total;
    }

    void Track(float rise, double time, float duration)
    {
        stepSeconds += (duration - stepSeconds) * 0.05f;
        onsets[onsetHead] = rise;
        onsetHead = (onsetHead + 1) % onsets.Length;
        onsetCount = Math.Min(onsets.Length, onsetCount + 1);
        steps++;
        if (steps % 25 != 0 || onsetCount < 400) return;
        int n = onsetCount;
        var x = new float[n];
        for (int i = 0; i < n; i++) x[i] = onsets[(onsetHead - n + i + onsets.Length * 2) % onsets.Length];
        float mean = 0; foreach (var v in x) mean += v; mean /= n;
        for (int i = 0; i < n; i++) x[i] -= mean;
        float zero = 0; foreach (var v in x) zero += v * v;
        if (zero <= 1e-6f) { Confidence *= 0.8f; return; }
        int minLag = Math.Max(2, (int)(0.30f / stepSeconds)), maxLag = Math.Min(n / 5, (int)(0.80f / stepSeconds));
        if (maxLag <= minLag + 2) return;
        var scores = new float[maxLag + 2];
        float Corr(int lag)
        {
            if (lag >= n) return 0;
            float s = 0;
            for (int i = lag; i < n; i++) s += x[i] * x[i - lag];
            return s / zero * n / (n - lag);
        }
        int best = minLag; float bestScore = float.MinValue;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float seconds = lag * stepSeconds;
            float l2 = MathF.Log2(seconds / 0.52f) / 0.9f;
            float prior = MathF.Exp(-0.5f * l2 * l2);                 // a gentle lean to ~115 BPM
            float s = (Corr(lag) + Corr(lag * 2) / 2 + Corr(lag * 4) / 2) * prior;   // songs count in 2s and 4s
            if (Period > 0 && MathF.Abs(seconds - Period) / Period < 0.04f) s *= 1.2f;
            scores[lag] = s;
            if (s > bestScore) { bestScore = s; best = lag; }
        }
        float lagF = best;
        if (best > minLag && best < maxLag)
        {
            float a = scores[best - 1], b = scores[best], c = scores[best + 1];
            float d = a - 2 * b + c;
            if (MathF.Abs(d) > 1e-9f) lagF += Math.Clamp(0.5f * (a - c) / d, -0.5f, 0.5f);
        }
        float sure = Math.Clamp(Corr(best) * 4, 0, 1);
        float newPeriod = lagF * stepSeconds;
        // A song's tempo hardly changes: follow the tempo heard most over ~30 s.
        float bpm = 60 / newPeriod;
        float decay = MathF.Exp(-0.25f / 30);
        foreach (var k in votes.Keys.ToList()) votes[k] *= decay;
        int key = (int)MathF.Round(bpm / 2) * 2;                        // 2-BPM buckets
        votes[key] = votes.GetValueOrDefault(key) + MathF.Max(0.05f, sure);
        votes = votes.Where(v => v.Value > 0.01f).ToDictionary(v => v.Key, v => v.Value);
        int bestKey = key; float bestVote = -1;
        foreach (var (k, v) in votes)
        {
            float near = v + votes.GetValueOrDefault(k - 2) * 0.5f + votes.GetValueOrDefault(k + 2) * 0.5f;
            if (near > bestVote) { bestVote = near; bestKey = k; }
        }
        float chosen = Math.Abs(key - bestKey) <= 2 ? newPeriod : 60f / bestKey;
        if (Period == 0 || MathF.Abs(chosen - Period) / Period > 0.04f) Period = chosen; else Period += (chosen - Period) * 0.3f;
        lagF = Period / stepSeconds;
        // Where the beats fall: the phase whose comb collects the most onset.
        int bins = Math.Max(1, (int)MathF.Round(lagF));
        var comb = new float[bins];
        for (int i = Math.Max(0, n - 400); i < n; i++)
        {
            float ph = i % lagF;
            comb[Math.Min(bins - 1, (int)ph)] += MathF.Max(0, x[i] + mean);
        }
        int topBin = 0;
        for (int i = 1; i < bins; i++) if (comb[i] > comb[topBin]) topBin = i;
        // Keep the beat where it was unless another place is clearly stronger.
        if (BeatTime > 0)
        {
            float since = (float)(time - BeatTime) / stepSeconds;
            float held = (n - 1) - (since - MathF.Floor(since / lagF) * lagF);
            int heldBin = Math.Clamp((int)MathF.Floor(held % lagF), 0, bins - 1);
            int nearBin = heldBin;
            foreach (var cand in new[] { (heldBin + 1) % bins, (heldBin + bins - 1) % bins }) if (comb[cand] > comb[nearBin]) nearBin = cand;
            if (comb[nearBin] >= comb[topBin] * 0.8f) topBin = nearBin;
        }
        float phase = topBin + 0.5f;
        float sinceBeat = (n - 1) - phase;
        float stepsSince = sinceBeat - MathF.Floor(sinceBeat / lagF) * lagF;
        Confidence += (sure - Confidence) * 0.4f;
        BeatTime = time - stepsSince * stepSeconds;
    }

    /// Returns (energy 0…1, beat pulse 0…1). Feed it ~10 ms at a time.
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
        Track(Flux(samples), time, duration);
        float db = 10 * MathF.Log10(MathF.Max(full, 1e-10f));
        if (db > -60)
        {
            if (longDB < -90) { shortDB = db; longDB = db; }
            heard += duration;
            shortDB += (db - shortDB) * MathF.Min(1, duration / 1.5f);
            float memory = shortDB > longDB ? 8 : 20;
            longDB += (shortDB - longDB) * MathF.Min(1, duration / MathF.Max(1.5f, MathF.Min(memory, heard)));
            Section = Math.Clamp((shortDB - longDB) / 6, -0.4f, 0.6f);
        }
        else Section *= MathF.Exp(-duration / 0.5f);
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

/// What the light needs from the audio.
public readonly record struct AudioSignal(float Energy, float Beat, double Age, float Period, float Confidence, double BeatTime, float Section);

/// Listens to what the PC is playing (never the microphone) and reports
/// loudness and beats. Restarts itself when the output device changes.
public sealed class AudioLevel : IDisposable
{
    WasapiLoopbackCapture? capture;
    BeatDetector detector = new();
    readonly object gate = new();
    float energy, beat, period, confidence, section;
    double stamp = -10, beatTime;
    float[] mono = Array.Empty<float>();
    bool wanted;
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    /// The clock beat times are measured on.
    public static double Now => clock.Elapsed.TotalSeconds;
    public bool Running => capture is not null;

    public AudioSignal Read()
    {
        lock (gate) return new(energy, beat, Now - stamp, period, confidence, beatTime, section);
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
        lock (gate) { energy = 0; beat = 0; period = 0; confidence = 0; section = 0; }
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
        double now = Now;
        // ~10 ms hops, so beats are caught with low latency and the tempo
        // tracker gets an even 100 steps a second.
        int hop = Math.Max(64, format.SampleRate / 100);
        float e = 0, b = 0;
        for (int offset = 0; offset < frames; offset += hop)
        {
            int count = Math.Min(hop, frames - offset);
            (e, b) = detector.Process(mono.AsSpan(offset, count), now + (double)offset / format.SampleRate);
        }
        lock (gate) { energy = e; beat = b; stamp = now; period = detector.Period; confidence = detector.Confidence; beatTime = detector.BeatTime; section = detector.Section; }
    }

    public void Dispose() => Stop();
}
