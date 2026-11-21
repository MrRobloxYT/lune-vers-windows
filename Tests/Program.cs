using LuneVers;

int failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }

var enhanced = Lrc.Parse("[00:01.00]<00:01.00>Hello <00:01.50>world\n[00:03.00]Next line here", 10);
Check(enhanced.Words.Count == 5 && Math.Abs(enhanced.Words[1].Start - 1.5) < 1e-6, "word timestamps");
Check(enhanced.Words[2].Start == 3.0 && enhanced.Words[4].End <= 10.0, "estimated timing within line");
Check(enhanced.LineCount == 2 && enhanced.Lines().Count == 2, "line grouping");
var offset = Lrc.Parse("[offset:+500]\n[00:02.00]Hi there", 10);
Check(Math.Abs(offset.Words[0].Start - 2.5) < 1e-6, "offset tag");
var repeat = Lrc.Parse("[00:01.00][00:05.00]Again", 10);
Check(repeat.Words.Count == 2 && repeat.Words[1].Start == 5, "repeated lines");
Check(Lrc.Parse("no timestamps").Words.Count == 0, "malformed input");
Check(Lrc.PlainTitle("Song (feat. X) - Remastered 2011") == "Song", "plain title");
Check(Lrc.MainArtist("A, B & C") == "A" && Lrc.MainArtist("A feat. B") == "A", "main artist");
var demo = Lrc.Parse(Demo.Lyrics, 40);
Check(demo.Words.Count > 30 && Lrc.LastStarted(demo.Words, 13) >= 0, "demo lyrics");
Check(Lrc.ActiveIndex(demo.Words, 0.5) == -1, "nothing before first line");

// Palette: a half red, half blue image gives red and blue.
var px = new byte[48 * 48 * 4];
for (int i = 0; i < 48 * 48; i++) { bool red = i % 48 < 28; px[i * 4 + 2] = (byte)(red ? 230 : 20); px[i * 4 + 1] = 30; px[i * 4] = (byte)(red ? 40 : 220); px[i * 4 + 3] = 255; }
var pal = Palette.FromPixels(px, 2);
Check(pal is { Count: 2 } && pal[0].R > pal[0].B && pal[1].B > pal[1].R, "album palette");
var c = Rgb.FromHsb(0.6f, 0.8f, 0.9f).Hsb();
Check(Math.Abs(c.H - 0.6f) < 0.01 && Math.Abs(c.S - 0.8f) < 0.01, "hsb round trip");

var beat = new BeatDetector(48000);
var quiet = new float[1024]; var loud = new float[1024];
for (int i = 0; i < 1024; i++) loud[i] = MathF.Sin(i * 2 * MathF.PI * 60 / 48000) * 0.8f;
for (int k = 0; k < 40; k++) beat.Process(quiet, k * 0.02);
var hit = beat.Process(loud, 1.0);
Check(hit.Beat > 0.5f && hit.Energy > 0, "beat onset");

// Tempo: kick drums at a known tempo; the tracker must find it and where the beats fall.
foreach (var bpm in new[] { 120.0, 92.0, 140.0 })
{
    var tempo = new BeatDetector(48000);
    var rng = new Random(1);
    var hopBuf = new float[480];
    double gap = 60 / bpm;
    for (int start = 0; start < 48000 * 20; start += 480)
    {
        for (int i = 0; i < 480; i++)
        {
            double tt = (start + i) / 48000.0, since = tt % gap;
            double kick = since < 0.12 ? Math.Sin(2 * Math.PI * 55 * since) * Math.Exp(-since * 25) * 0.8 : 0;
            hopBuf[i] = (float)(kick + (rng.NextDouble() - 0.5) * 0.01);
        }
        tempo.Process(hopBuf, start / 48000.0);
    }
    double found = tempo.Period > 0 ? 60 / tempo.Period : 0;
    double off = tempo.BeatTime % gap; off = Math.Min(off, gap - off);
    Console.WriteLine($"  tempo {bpm}: found {found:0.0} bpm, sure {tempo.Confidence:0.00}, beat off by {off * 1000:0} ms");
    Check(Math.Abs(found - bpm) < 2.5 && tempo.Confidence > 0.3, $"tempo {bpm} bpm");
    Check(off < 0.04, $"beat position at {bpm} bpm");
}

Check(MediaWatcher.FriendlyName("Spotify.exe") == "Spotify" && MediaWatcher.FriendlyName("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") == "Apple Music", "player names");

foreach (var app in new[] { "Lune", "Vers" })
{
    var found = await Updater.FindAsync(app);
    Console.WriteLine($"  updater sees {app}: {found?.Version} {found?.Url}");
    Check(found is { } f && f.Version >= new Version(1, 3, 0) && f.Sha256.Length == 64 && f.Url.EndsWith($"{app}-Setup.exe"), $"updater finds {app}");
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
