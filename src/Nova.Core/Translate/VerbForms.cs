namespace Nova.Core.Translate;

/// <summary>The five forms of an English verb, worked out offline (a table of irregular verbs plus spelling rules for regular ones).</summary>
public sealed record VerbForms(string V1, string V2, string V3, string Ing, string S)
{
    /// <summary>"Verb 1 : go" … one form per line, for the notch.</summary>
    public string Format() => $"Verb 1 : {V1}\nVerb 2 : {V2}\nVerb 3 : {V3}\nVerb-ing : {Ing}\nVerb-s : {S}";

    // base past participle ("/" separates alternatives)
    const string Irregular =
        "be was/were been;become became become;begin began begun;bend bent bent;bite bit bitten;blow blew blown;break broke broken;bring brought brought;build built built;" +
        "burn burnt/burned burnt/burned;buy bought bought;catch caught caught;choose chose chosen;come came come;cost cost cost;cut cut cut;deal dealt dealt;dig dug dug;do did done;" +
        "draw drew drawn;drink drank drunk;drive drove driven;eat ate eaten;fall fell fallen;feed fed fed;feel felt felt;fight fought fought;find found found;fly flew flown;" +
        "forget forgot forgotten;forgive forgave forgiven;freeze froze frozen;get got got/gotten;give gave given;go went gone;grow grew grown;hang hung hung;have had had;hear heard heard;" +
        "hide hid hidden;hit hit hit;hold held held;hurt hurt hurt;keep kept kept;know knew known;lay laid laid;lead led led;leave left left;lend lent lent;let let let;lose lost lost;" +
        "make made made;mean meant meant;meet met met;overcome overcame overcome;pay paid paid;put put put;quit quit quit;read read read;ride rode ridden;ring rang rung;rise rose risen;" +
        "run ran run;say said said;see saw seen;seek sought sought;sell sold sold;send sent sent;set set set;shake shook shaken;shine shone shone;shoot shot shot;show showed shown;" +
        "shut shut shut;sing sang sung;sink sank sunk;sit sat sat;sleep slept slept;slide slid slid;speak spoke spoken;spend spent spent;spin spun spun;split split split;spread spread spread;" +
        "stand stood stood;steal stole stolen;stick stuck stuck;strike struck struck;swear swore sworn;sweep swept swept;swim swam swum;swing swung swung;take took taken;teach taught taught;" +
        "tear tore torn;tell told told;think thought thought;throw threw thrown;understand understood understood;wake woke woken;wear wore worn;win won won;withdraw withdrew withdrawn;write wrote written";

    // Regular verbs that are common enough to show without "to" in front.
    const string Regular =
        "work walk talk play look want need use help start stop ask call try turn move live like love open close watch learn study wait change clean cook cry dance decide enjoy finish follow " +
        "happen hope jump kiss listen miss mix notice offer order paint plan plant pull push remember repeat reply save share smile stay carry marry worry copy visit wash pass pick pray prepare " +
        "print promise practice rain reach receive return rent rest roll rush search shop sound taste thank touch train travel treat trust type update wish wonder answer arrive believe borrow " +
        "check climb collect connect continue count create deliver describe develop die discuss download drop earn explain fail fix force guess hate invite join kill knock laugh lift lock " +
        "manage mark matter measure mention name obey park post pour produce protect provide race record relax remove report respect rub serve shout sign slip stare suggest support surprise " +
        "test tie tip wipe yell admit permit prefer refer occur commit submit regret control equip";

    // Several-syllable verbs whose last consonant is doubled anyway (admit → admitted); "quit" is one syllable but spelled with qu.
    static readonly HashSet<string> ForceDouble = new() { "admit", "permit", "prefer", "refer", "occur", "commit", "submit", "regret", "control", "equip", "begin", "forget", "quit", "upset" };

    static readonly Dictionary<string, (string Past, string Pp)> IrregularByBase = ParseIrregular();
    static readonly Dictionary<string, string> BaseByForm = BuildReverse();
    static readonly HashSet<string> RegularSet = new(Regular.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static Dictionary<string, (string, string)> ParseIrregular()
    {
        var d = new Dictionary<string, (string, string)>();
        foreach (var row in Irregular.Split(';'))
        {
            var p = row.Split(' ');
            d[p[0]] = (p[1], p[2]);
        }
        return d;
    }

    static Dictionary<string, string> BuildReverse()
    {
        var d = new Dictionary<string, string>();
        foreach (var (b, (past, pp)) in IrregularByBase)
            foreach (var form in new[] { b }.Concat(past.Split('/')).Concat(pp.Split('/')))
                d.TryAdd(form, b);   // the first base wins when forms coincide ("read", "put")
        return d;
    }

    static bool IsVowel(char c) => "aeiou".Contains(c);

    static int VowelGroups(string w)
    {
        int n = 0; bool inGroup = false;
        foreach (var c in w) { var v = IsVowel(c); if (v && !inGroup) n++; inGroup = v; }
        return n;
    }

    /// <summary>Ends in consonant–vowel–consonant (not w, x, y) and is one syllable (or a known exception): the last letter is doubled before -ed / -ing.</summary>
    static bool DoublesLast(string w)
    {
        if (ForceDouble.Contains(w)) return true;
        if (w.Length < 3 || VowelGroups(w) != 1) return false;
        char a = w[^3], b = w[^2], c = w[^1];
        return !IsVowel(a) && IsVowel(b) && !IsVowel(c) && c is not ('w' or 'x' or 'y');
    }

    static string RegularPast(string w) =>
        w.EndsWith('e') ? w + "d"
        : w.Length > 1 && w[^1] == 'y' && !IsVowel(w[^2]) ? w[..^1] + "ied"
        : DoublesLast(w) ? w + w[^1] + "ed"
        : w + "ed";

    static string RegularIng(string w) =>
        w.EndsWith("ie") ? w[..^2] + "ying"
        : w.EndsWith('e') && !w.EndsWith("ee") && !w.EndsWith("ye") && !w.EndsWith("oe") ? w[..^1] + "ing"
        : DoublesLast(w) ? w + w[^1] + "ing"
        : w + "ing";

    static string ThirdPerson(string w) =>
        w == "have" ? "has" : w == "be" ? "is"
        : w.EndsWith("ch") || w.EndsWith("sh") || w[^1] is 's' or 'x' or 'z' or 'o' ? w + "es"
        : w.Length > 1 && w[^1] == 'y' && !IsVowel(w[^2]) ? w[..^1] + "ies"
        : w + "s";

    /// <summary>All forms of a base verb (any lower-case English word; regular spelling rules are used when it is not in the irregular table).</summary>
    public static VerbForms Build(string baseForm)
    {
        var w = baseForm.ToLowerInvariant();
        string past, pp;
        if (IrregularByBase.TryGetValue(w, out var irr)) (past, pp) = irr;
        else { past = RegularPast(w); pp = past; }
        return new VerbForms(w, past, pp, w == "be" ? "being" : RegularIng(w), ThirdPerson(w));
    }

    /// <summary>
    /// Looks for a verb to show forms of: the translation when it is English (target "en"), otherwise the typed word. Only a single word counts:
    /// a known irregular verb in any of its forms ("went" → go), a common regular verb, or any word written as "to walk".
    /// </summary>
    public static bool TryFromTranslation(string source, string targetCode, string translated, out VerbForms forms)
    {
        forms = null!;
        var candidates = string.Equals(targetCode, "en", StringComparison.OrdinalIgnoreCase) ? new[] { translated } : new[] { source };
        foreach (var raw in candidates)
        {
            var t = (raw ?? "").Trim().Trim('.', ',', '!', '?', ';', ':').ToLowerInvariant();
            bool explicitVerb = false;
            if (t.StartsWith("to ", StringComparison.Ordinal)) { t = t[3..].Trim(); explicitVerb = true; }
            if (t.Length < 2 || t.Length > 20 || !t.All(c => c is >= 'a' and <= 'z')) continue;   // one plain English word
            if (BaseByForm.TryGetValue(t, out var b)) { forms = Build(b); return true; }
            if (explicitVerb || RegularSet.Contains(t)) { forms = Build(t); return true; }
        }
        return false;
    }
}
