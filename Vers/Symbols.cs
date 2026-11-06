namespace Vers;

/// Word → icon map for the Visual styles, the same words as the Mac app,
/// drawn with Windows' color emoji. Lookups also try simple inflections
/// ("cars", "driving", "loved", "fallin'"), so each entry covers several forms.
public static class Symbols
{
    static readonly Dictionary<string, string> Map = Build();

    public static string? For(string word)
    {
        var key = new string(word.ToLowerInvariant().Trim().SkipWhile(c => !char.IsLetter(c)).Reverse().SkipWhile(c => !char.IsLetter(c) && c != '\'').Reverse().ToArray());
        if (key.Length <= 1) return null;
        foreach (var candidate in Forms(key)) if (Map.TryGetValue(candidate, out var hit)) return hit;
        return null;
    }

    static IEnumerable<string> Forms(string k)
    {
        yield return k;
        if (k.EndsWith("in")) yield return k + "g";
        if (k.EndsWith("in'")) yield return k[..^1] + "g";
        if (k.EndsWith("ies")) yield return k[..^3] + "y";
        if (k.EndsWith("es")) yield return k[..^2];
        if (k.EndsWith("s")) yield return k[..^1];
        if (k.EndsWith("ing")) { var b = k[..^3]; yield return b; yield return b + "e"; if (b.Length > 2 && b[^1] == b[^2]) yield return b[..^1]; }
        if (k.EndsWith("ed")) { var b = k[..^2]; yield return b; yield return b + "e"; yield return k[..^1]; if (b.Length > 2 && b[^1] == b[^2]) yield return b[..^1]; }
    }

    static Dictionary<string, string> Build()
    {
        var m = new Dictionary<string, string>();
        void Add(string icon, string words) { foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) m[w] = icon; }
        Add("❤️", "love lover lovin heart hearts darling sweetheart valentine romance adore");
        Add("💔", "heartbreak heartbroken broken breakup");
        Add("💘", "crush passion");
        Add("😊", "smile smiling happy happiness joy laugh laughing grin");
        Add("🌧️", "cry crying cried tears tear sad sadness sorrow weep rain raining rainy storm");
        Add("🔥", "fire flame flames burn burning hot heat lit blaze ignite");
        Add("❄️", "cold ice icy freeze frozen snow winter chill");
        Add("⚡", "lightning electric shock thunder energy power");
        Add("✨", "magic sparkle shine shining glitter glow glowing dazzle wonder wish");
        Add("⭐", "star stars superstar famous fame");
        Add("🌌", "night tonight midnight nights");
        Add("🌙", "moon moonlight lunar");
        Add("☀️", "sun sunny sunshine summer daylight bright");
        Add("🌅", "morning sunrise dawn wake awake");
        Add("🌇", "sunset evening dusk");
        Add("☁️", "cloud clouds cloudy sky skies heaven");
        Add("🌬️", "wind blow blew blown breeze breath breathe air storming");
        Add("🌪️", "tornado hurricane chaos");
        Add("🌈", "rainbow colors colour colours");
        Add("💧", "water drop drops wet drown drowning drink");
        Add("🌊", "ocean sea wave waves tide river swim swimming");
        Add("🍃", "leaf leaves tree trees nature green garden grow");
        Add("🌸", "flower flowers rose roses bloom blossom petal");
        Add("🌎", "world earth globe planet universe");
        Add("⛰️", "mountain mountains hill climb high");
        Add("🧠", "mind think thinking thought thoughts brain crazy insane memory remember forget");
        Add("👁️", "see eye eyes look looking watch watching stare saw seen sight vision");
        Add("👂", "hear listen listening heard ear ears");
        Add("👄", "mouth lips kiss kisses kissing taste");
        Add("✋", "hand hands stop wave hello hi");
        Add("☝️", "touch touching swish tap point");
        Add("👏", "clap clapping applause");
        Add("👍", "okay ok good great yes yeah");
        Add("👎", "bad wrong");
        Add("🚶", "walk walking walked step steps away leave leaving gone");
        Add("🏃", "run running ran chase chasing escape hurry fast");
        Add("💃", "dance dancing dancer party move moving groove");
        Add("🍂", "fall falling fell drop dropping");
        Add("🫂", "free freedom alive");
        Add("👫", "together us couple friends friend");
        Add("👤", "you me i myself yourself person somebody someone body man woman boy girl he she him her baby babe");
        Add("👥", "people crowd everybody everyone they them we");
        Add("👑", "king queen royal crown prince princess throne");
        Add("🏆", "win winner winning champion trophy victory");
        Add("🏠", "home house room bedroom roof");
        Add("🏙️", "city town street streets downtown");
        Add("🚪", "door open close closed knock");
        Add("🛏️", "bed sleep sleeping asleep dream dreams dreaming tired");
        Add("🚗", "car cars drive driving drove ride riding road highway");
        Add("🚌", "bus");
        Add("🚋", "train subway");
        Add("✈️", "fly flying flew plane airplane flight jet");
        Add("🚲", "bike bicycle");
        Add("⛵", "boat sail sailing ship");
        Add("🗺️", "map lost find found way direction directions");
        Add("📍", "here there place where");
        Add("🪧", "road roads path crossroads");
        Add("📞", "phone call calling called ring ringing");
        Add("💬", "text texting message messages tell telling told talk talking said say saying speak");
        Add("💬", "conversation chat");
        Add("✉️", "letter mail write wrote");
        Add("✏️", "pen pencil");
        Add("📖", "book books story stories page read reading");
        Add("📺", "tv television screen");
        Add("📷", "camera photo picture pictures");
        Add("🎵", "music song songs sing singing sang melody tune");
        Add("🎤", "mic microphone voice");
        Add("🎧", "headphones listen");
        Add("🎸", "guitar band rock");
        Add("🎹", "piano keys");
        Add("🔊", "loud louder sound noise radio");
        Add("🔇", "silence silent quiet");
        Add("🕒", "time minute minutes hour hours clock late early");
        Add("⏳", "wait waiting patience forever");
        Add("📅", "day days week weekend month year years today tomorrow yesterday");
        Add("⏰", "alarm");
        Add("🎁", "gift present birthday");
        Add("🎈", "celebrate celebration balloons");
        Add("🎉", "party parties");
        Add("🍷", "wine drunk champagne cheers");
        Add("☕", "coffee tea");
        Add("🍴", "eat eating food dinner hungry");
        Add("🎂", "cake sweet sugar");
        Add("🛒", "buy shop shopping");
        Add("💵", "money cash dollar dollars rich paid pay");
        Add("💳", "card");
        Add("💎", "diamond diamonds jewel ice");
        Add("👜", "bag");
        Add("👕", "shirt clothes dress wear");
        Add("👓", "glasses");
        Add("🔑", "key keys lock unlock");
        Add("🔒", "locked secret secrets");
        Add("🛡️", "safe protect protection");
        Add("🎮", "game games play playing played");
        Add("🎲", "chance chances luck lucky gamble roll");
        Add("🎲", "risk");
        Add("🏀", "ball ballin basketball");
        Add("⚽", "soccer football");
        Add("🏅", "gold medal");
        Add("🚩", "flag");
        Add("🔔", "bell bells");
        Add("💡", "idea light lights");
        Add("🔋", "charge battery");
        Add("📶", "connect connection signal");
        Add("📡", "frequency");
        Add("🕸️", "online internet");
        Add("📱", "iphone");
        Add("💻", "computer laptop");
        Add("⚙️", "machine engine");
        Add("🔨", "build building break breaking");
        Add("🔧", "fix fixing");
        Add("✂️", "cut");
        Add("🖌️", "paint painting art color");
        Add("🎭", "drama fake mask");
        Add("🎬", "movie movies film");
        Add("🎟️", "ticket show");
        Add("🛫", "leaving goodbye bye");
        Add("⬆️", "up rise rising higher");
        Add("⬇️", "down low lower");
        Add("⬅️", "back");
        Add("➡️", "forward next");
        Add("🔁", "again repeat replay over");
        Add("🔄", "change changes changing turn around");
        Add("↩️", "return");
        Add("#️⃣", "number numbers count");
        Add("❓", "why what who how question questions");
        Add("⚠️", "danger warning trouble");
        Add("❌", "no never nothing nobody");
        Add("✅", "right sure true");
        Add("♾️", "infinite endless always eternity");
        Add("⚪", "circle");
        Add("⬛", "square");
        Add("🔺", "triangle");
        Add("⬢", "hexagon");
        Add("🐾", "dog dogs cat cats pet");
        Add("🐦", "bird birds wings wing");
        Add("🐟", "fish");
        Add("🐜", "bug");
        Add("🐢", "slow");
        Add("🐇", "quick");
        Add("🦎", "snake");
        Add("🐞", "butterfly");
        Add("✝️", "god pray prayer church angel angels holy");
        Add("🧘", "peace calm");
        Add("🩹", "hurt hurts hurting pain wound scar scars heal");
        Add("🧰", "doctor");
        Add("💊", "pill pills medicine");
        Add("💉", "needle");
        Add("💓", "heartbeat beat beats pulse");
        Add("🫁", "lungs");
        Add("⚠️", "dead die dying death kill killer ghost");
        Add("🙈", "blind hide hiding");
        Add("🔍", "search searching seek");
        Add("🔭", "far distance");
        Add("🎯", "target aim");
        Add("⚡", "speed");
        Add("🏎️", "faster");
        Add("⛽", "gas");
        Add("🛣️", "lane lanes");
        Add("🚘", "wheel");
        Add("🅿️", "park");
        Add("⛴️", "ferry");
        Add("⛺", "camp");
        Add("🏖️", "beach");
        Add("🌤️", "haze");
        Add("🌫️", "fog smoke");
        Add("🌡️", "fever");
        Add("☂️", "umbrella");
        Add("👋", "waving");
        Add("🧍", "stand standing alone lonely");
        Add("🙌", "hug hold holding embrace");
        Add("🌀", "roller");
        Add("👣", "footsteps");
        Add("🙏", "blessed");
        Add("🧠", "smart");
        Add("💤", "sleepy");
        Add("😴", "rest");
        Add("🎉", "fun");
        Add("🎶", "rhythm");
        Add("🎼", "tempo");
        Add("📻", "station");
        Add("🎤", "rap rapper");
        Add("📶", "vibe vibes");
        Add("⚡", "electricity");
        Add("⚛️", "science");
        Add("🌐", "global");
        Add("✍️", "name names sign");
        Add("👋", "farewell");
        Add("😶", "nobody's");
        return m;
    }
}
