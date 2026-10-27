using System.Globalization;

namespace Nova.Core.Launch;

/// <summary>One thing the quick launcher can open: a program shortcut, a pinned dock app, a folder.</summary>
public sealed record LauncherEntry(string Name, string Path);

/// <summary>
/// The brain of the quick launcher, with no UI: ranks entries against what was typed, evaluates a small arithmetic expression
/// ("= 12*(3+4)" or just "12*(3+4)"), and recognises web addresses. Everything is pure so it can be unit-tested.
/// </summary>
public static class LauncherSearch
{
    /// <summary>0 = no match. Higher is better: exact name, name starts with it, a word starts with it, contains it, letters in order.</summary>
    public static int Score(string query, string name)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0 || string.IsNullOrEmpty(name)) return 0;
        var q = query.ToLowerInvariant();
        var n = name.ToLowerInvariant();

        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 800 - Math.Min(100, n.Length - q.Length);

        // a later word starts with the query: "code" finds "Visual Studio Code"
        int wordAt = -1;
        for (int i = 1; i < n.Length; i++)
            if (!char.IsLetterOrDigit(n[i - 1]) && n.AsSpan(i).StartsWith(q.AsSpan(), StringComparison.Ordinal)) { wordAt = i; break; }
        if (wordAt >= 0) return 600 - Math.Min(100, wordAt);

        int at = n.IndexOf(q, StringComparison.Ordinal);
        if (at >= 0) return 400 - Math.Min(100, at);

        // letters in order ("vsc" finds "Visual Studio Code"); consecutive letters count more
        int pos = 0, run = 0, best = 0, hit = 0;
        foreach (var ch in q)
        {
            if (char.IsWhiteSpace(ch)) continue;
            int next = n.IndexOf(ch, pos);
            if (next < 0) return 0;
            run = next == pos ? run + 1 : 1;
            best = Math.Max(best, run);
            hit++;
            pos = next + 1;
        }
        return hit == 0 ? 0 : 100 + best * 10;
    }

    /// <summary>Best matches first; ties go to the shorter name, then alphabetical. Duplicate paths are shown once.</summary>
    public static IReadOnlyList<LauncherEntry> Rank(IEnumerable<LauncherEntry> entries, string query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<LauncherEntry>();
        return entries
            .Select(e => (Entry: e, Score: Score(query, e.Name)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Name.Length)
            .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, max))
            .ToList();
    }

    /// <summary>http(s) addresses and "www." names. Anything else (no scheme, spaces, other schemes such as file:) is not a web address.</summary>
    public static bool TryGetWebAddress(string? text, out string url)
    {
        url = "";
        var t = (text ?? "").Trim();
        if (t.Length < 4 || t.Length > 2000 || t.Any(char.IsWhiteSpace)) return false;
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (!u.Host.Contains('.') ) return false;
        url = u.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// Evaluates + - * / % ^ and parentheses on decimal numbers (no names, no functions: it can run nothing).
    /// A leading "=" is allowed. Returns false for anything that is not a complete, valid expression or that divides by zero.
    /// </summary>
    public static bool TryEvaluate(string? text, out string result)
    {
        result = "";
        var s = (text ?? "").Trim();
        if (s.StartsWith('=')) s = s[1..].Trim();
        if (s.Length == 0 || s.Length > 200) return false;
        if (!s.Any(c => "+-*/%^".Contains(c))) return false;   // a bare number is not worth showing
        var p = new Parser(s.Replace(',', '.'));
        if (!p.TryParse(out var v) || double.IsNaN(v) || double.IsInfinity(v)) return false;
        result = Math.Round(v, 10).ToString("0.##########", CultureInfo.InvariantCulture);
        return true;
    }

    sealed class Parser
    {
        readonly string _s; int _i;
        public Parser(string s) { _s = s; }

        public bool TryParse(out double value)
        {
            value = 0;
            try
            {
                value = Sum();
                Skip();
                return _i == _s.Length;
            }
            catch (FormatException) { return false; }
            catch (DivideByZeroException) { return false; }
        }

        void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        bool Take(char c) { Skip(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }

        double Sum()
        {
            var v = Product();
            while (true)
            {
                if (Take('+')) v += Product();
                else if (Take('-')) v -= Product();
                else return v;
            }
        }

        double Product()
        {
            var v = Power();
            while (true)
            {
                if (Take('*')) v *= Power();
                else if (Take('/')) { var d = Power(); if (d == 0) throw new DivideByZeroException(); v /= d; }
                else if (Take('%')) { var d = Power(); if (d == 0) throw new DivideByZeroException(); v %= d; }
                else return v;
            }
        }

        double Power()
        {
            var b = Unary();
            if (Take('^')) { var e = Power(); return Math.Pow(b, e); }   // right-associative
            return b;
        }

        double Unary()
        {
            if (Take('-')) return -Unary();
            if (Take('+')) return Unary();
            return Atom();
        }

        double Atom()
        {
            if (Take('('))
            {
                var v = Sum();
                if (!Take(')')) throw new FormatException();
                return v;
            }
            Skip();
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (start == _i) throw new FormatException();
            if (!double.TryParse(_s.AsSpan(start, _i - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)) throw new FormatException();
            return n;
        }
    }
}
