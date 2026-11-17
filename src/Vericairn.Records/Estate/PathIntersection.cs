// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Estate;

/// <summary>
/// The exact overlap of two assignments' effective permitted paths (from 0.9.4, CODE-73; the estate's design/measured-work.md § 5):
/// whether some valid path matches a permitted pattern of each party, no exclusion of either and, under a selection, one of its
/// selectors. Each party's patterns and exclusions are compiled from the worker's grammar, the selection's selectors from the policy
/// grammar, and the valid paths from <see cref="PathPatterns.Valid"/>'s rules, each to a finite automaton over path characters; the
/// automata are determinized lazily by subset construction, which complements the exclusions, and their product is searched breadth
/// first for an accepted path. Every subset state and every product state counts toward a bound; a search that reaches it reports the
/// pair as overlapping, undetermined. The length limit of 2,000 characters is not compiled in: the search returns the shortest accepted
/// path, so a shortest path longer than the limit means no valid overlap exists. Matching is ordinal, as the worker's is.
/// </summary>
internal static class PathIntersection
{
    /// <summary>The bound on subset and product states together.</summary>
    internal const int StateBound = 100_000;

    /// <summary>The length limit of a valid path, checked on the shortest witness.</summary>
    internal const int LengthLimit = 2000;

    /// <summary>One party: its interpretable permitted patterns and its exclusions, each in the worker's grammar.</summary>
    /// <param name="Patterns">The patterns it is granted.</param>
    /// <param name="Exclusions">The patterns it excludes, including any the comparison leaves out for it.</param>
    internal sealed record Party(IReadOnlyList<string> Patterns, IReadOnlyList<string> Exclusions)
    {
        /// <summary>Whether a path is one of the party's effective paths: valid, matched by a pattern and by no exclusion.</summary>
        internal bool Contains(string path) => PathPatterns.Valid(path) && Patterns.Any(p => PathPatterns.Matches(path, p)) && !Exclusions.Any(x => PathPatterns.Matches(path, x));
    }

    /// <summary>A search's outcome.</summary>
    /// <param name="Overlap">True when a valid path is in both parties' effective paths, or when the search could not decide.</param>
    /// <param name="Determined">False when the search reached its state bound; such a pair counts as overlapping.</param>
    /// <param name="Witness">The shortest such path when one was found, the first among the shortest in the search's order.</param>
    /// <param name="States">The subset and product states the search counted.</param>
    internal sealed record Outcome(bool Overlap, bool Determined, string? Witness, int States);

    /// <summary>Whether a path matches a selector of a selection, in the policy grammar; with no selection, every path does.</summary>
    internal static bool Selected(IReadOnlyList<string>? selectors, string path) => selectors is null || selectors.Any(s => PathPatterns.SelectorMatches(s, path));

    /// <summary>Searches the intersection of two parties' effective paths, within a selection's selectors when one is given.</summary>
    internal static Outcome Search(Party first, Party second, IReadOnlyList<string>? selectors = null, int bound = StateBound)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Patterns.Count == 0 || second.Patterns.Count == 0 || selectors is { Count: 0 })
        {
            return new Outcome(false, true, null, 0);
        }

        Alphabet alphabet = Alphabet.Of([.. first.Patterns, .. first.Exclusions, .. second.Patterns, .. second.Exclusions], selectors ?? []);
        var counter = new Counter();
        Lazy[] parts =
        [
            new(Nfa.Worker(first.Patterns), alphabet, counter),
            new(Nfa.Worker(first.Exclusions), alphabet, counter),
            new(Nfa.Worker(second.Patterns), alphabet, counter),
            new(Nfa.Worker(second.Exclusions), alphabet, counter),
            new(selectors is null ? Nfa.Universal() : Nfa.Selectors(selectors), alphabet, counter),
        ];

        var start = new State(parts[0].Start, parts[1].Start, parts[2].Start, parts[3].Start, parts[4].Start, ValidPaths.Start);
        var index = new Dictionary<State, int> { [start] = 0 };
        var states = new List<State> { start };
        var parents = new List<(int Parent, int Symbol)> { (-1, -1) };
        counter.Count++;
        var frontier = new List<int> { 0 };
        for (int depth = 0; depth < LengthLimit && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (int at in frontier)
            {
                State state = states[at];
                for (int symbol = 0; symbol < alphabet.Count; symbol++)
                {
                    int valid = ValidPaths.Step(state.Valid, alphabet.Chars[symbol]);
                    if (valid == ValidPaths.Dead) continue;
                    int a = parts[0].Step(state.A, symbol);
                    int b = parts[2].Step(state.B, symbol);
                    int s = parts[4].Step(state.S, symbol);
                    if (counter.Count >= bound) return new Outcome(true, false, null, counter.Count);
                    if (parts[0].Empty(a) || parts[2].Empty(b) || parts[4].Empty(s)) continue;
                    var reached = new State(a, parts[1].Step(state.ExcludedA, symbol), b, parts[3].Step(state.ExcludedB, symbol), s, valid);
                    if (index.ContainsKey(reached)) continue;
                    if (counter.Count >= bound) return new Outcome(true, false, null, counter.Count);
                    counter.Count++;
                    index[reached] = states.Count;
                    states.Add(reached);
                    parents.Add((at, symbol));
                    if (ValidPaths.Accepting(valid) && parts[0].Accepting(a) && !parts[1].Accepting(reached.ExcludedA) && parts[2].Accepting(b)
                        && !parts[3].Accepting(reached.ExcludedB) && parts[4].Accepting(s))
                    {
                        return new Outcome(true, true, Witness(states.Count - 1), counter.Count);
                    }

                    next.Add(states.Count - 1);
                }
            }

            frontier = next;
        }

        // No accepted path of at most the length limit: none is valid.
        return new Outcome(false, true, null, counter.Count);

        string Witness(int at)
        {
            var chars = new List<char>();
            for (int i = at; parents[i].Parent >= 0; i = parents[i].Parent)
            {
                chars.Add(alphabet.Chars[parents[i].Symbol]);
            }

            chars.Reverse();
            return new string([.. chars]);
        }
    }

    private sealed class Counter
    {
        public int Count;
    }

    private readonly record struct State(int A, int ExcludedA, int B, int ExcludedB, int S, int Valid);

    /// <summary>
    /// The path characters the search distinguishes: each literal character of a pattern or selector, the slash and the dot, and one
    /// representative each of the other non-whitespace and whitespace characters a valid path may hold; control characters, the
    /// backslash and the colon never occur in a valid path. Ordered so that the first shortest witness reads plainly: other characters by
    /// ordinal, then the dot and the slash, then whitespace.
    /// </summary>
    private sealed class Alphabet
    {
        private Alphabet(char[] chars) => Chars = chars;

        public char[] Chars { get; }

        public int Count => Chars.Length;

        public static Alphabet Of(IEnumerable<string> patterns, IEnumerable<string> selectors)
        {
            var literal = new HashSet<char> { '/', '.' };
            foreach (string pattern in patterns) literal.UnionWith(pattern.Where(c => c is not ('*' or '?')));
            foreach (string selector in selectors) literal.UnionWith(selector.Where(c => c != '*'));
            literal.RemoveWhere(c => !Allowed(c));
            const string others = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-+=@,";
            char? other = others.Cast<char?>().FirstOrDefault(c => !literal.Contains(c!.Value))
                ?? Enumerable.Range(0x00C0, 0x2000).Select(i => (char)i).Cast<char?>().FirstOrDefault(c => Allowed(c!.Value) && !char.IsWhiteSpace(c.Value) && !literal.Contains(c.Value));
            char? space = " \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000".Cast<char?>()
                .FirstOrDefault(c => !literal.Contains(c!.Value));
            var chars = new List<char>(literal);
            if (other is char o) chars.Add(o);
            if (space is char w) chars.Add(w);
            chars.Sort((x, y) => Rank(x) != Rank(y) ? Rank(x).CompareTo(Rank(y)) : x.CompareTo(y));
            return new Alphabet([.. chars]);
        }

        private static bool Allowed(char c) => !char.IsControl(c) && c is not ('\\' or ':');

        // Letters and other characters first, then the dot and the slash, whitespace last, so that a witness reads plainly.
        private static int Rank(char c) => char.IsWhiteSpace(c) ? 2 : c is '.' or '/' ? 1 : 0;
    }

    /// <summary>The valid-path language as a deterministic automaton: the component so far, and whether a non-whitespace character occurred.</summary>
    private static class ValidPaths
    {
        // Component kinds: 0 start, 1 empty (just after a slash), 2 ".", 3 "..", 4 other; times two for the non-whitespace flag.
        public const int Dead = -1;
        public const int Start = 0;

        public static int Step(int state, char c)
        {
            int component = state >> 1;
            bool seen = (state & 1) == 1;
            int next;
            if (c == '/')
            {
                if (component != 4) return Dead;
                next = 1;
            }
            else if (c == '.')
            {
                next = component switch { 0 or 1 => 2, 2 => 3, _ => 4 };
            }
            else
            {
                next = 4;
            }

            return next << 1 | (seen || !char.IsWhiteSpace(c) ? 1 : 0);
        }

        public static bool Accepting(int state) => state >> 1 == 4 && (state & 1) == 1;
    }

    /// <summary>A nondeterministic automaton over path characters, with epsilon moves; edges match one character, any but a slash, or any.</summary>
    private sealed class Nfa
    {
        private const int Literal = 0;
        private const int NotSlash = 1;
        private const int Any = 2;

        public List<List<(int Kind, char Char, int To)>> Edges { get; } = [];

        public List<List<int>> Epsilon { get; } = [];

        public List<bool> Accept { get; } = [];

        public int Start { get; private init; }

        public static bool Matches(int kind, char edge, char c) => kind switch { Literal => edge == c, NotSlash => c != '/', _ => true };

        /// <summary>The union of a list of the worker's patterns; with none, the empty language.</summary>
        public static Nfa Worker(IEnumerable<string> patterns)
        {
            var nfa = new Nfa { Start = 0 };
            nfa.Add();
            foreach (string pattern in patterns)
            {
                int start = nfa.Add();
                nfa.Epsilon[nfa.Start].Add(start);
                nfa.AddWorker(start, pattern);
            }

            return nfa;
        }

        /// <summary>The union of a selection's selectors in the policy grammar.</summary>
        public static Nfa Selectors(IEnumerable<string> selectors)
        {
            var nfa = new Nfa { Start = 0 };
            nfa.Add();
            foreach (string selector in selectors)
            {
                int start = nfa.Add();
                nfa.Epsilon[nfa.Start].Add(start);
                nfa.AddSelector(start, selector);
            }

            return nfa;
        }

        /// <summary>Every path.</summary>
        public static Nfa Universal()
        {
            var nfa = new Nfa { Start = 0 };
            nfa.Add();
            nfa.Edges[0].Add((Any, '\0', 0));
            nfa.Accept[0] = true;
            return nfa;
        }

        private int Add()
        {
            Edges.Add([]);
            Epsilon.Add([]);
            Accept.Add(false);
            return Edges.Count - 1;
        }

        private int Next(int from, int kind, char c = '\0')
        {
            int to = Add();
            Edges[from].Add((kind, c, to));
            return to;
        }

        private int Loop(int from, int kind)
        {
            int to = Add();
            Epsilon[from].Add(to);
            Edges[to].Add((kind, '\0', to));
            return to;
        }

        /// <summary>
        /// One pattern of the worker's grammar, token by token as <see cref="PathPatterns.Matches"/> reads it: a literal without wildcards
        /// matches the path and everything below it; otherwise ? is one character but a slash, * any run of them, ** any run of characters
        /// and **/ nothing or any run ending in a slash, which take priority as there, so that ***/ is a segment star and a directory star.
        /// </summary>
        private void AddWorker(int start, string pattern)
        {
            string plain = pattern.TrimEnd('/');
            int s = start;
            if (!plain.Contains('*', StringComparison.Ordinal) && !plain.Contains('?', StringComparison.Ordinal))
            {
                foreach (char c in plain) s = Next(s, Literal, c);
                Accept[s] = true;
                int below = Next(s, Literal, '/');
                Edges[below].Add((Any, '\0', below));
                Accept[below] = true;
                return;
            }

            for (int i = 0; i < plain.Length; i++)
            {
                char token = plain[i];
                if (token == '*')
                {
                    bool recursive = i + 1 < plain.Length && plain[i + 1] == '*'
                        && !(i + 3 < plain.Length && plain[i + 2] == '*' && plain[i + 3] == '/');
                    if (recursive) i++;
                    bool directory = recursive && i + 1 < plain.Length && plain[i + 1] == '/';
                    if (directory) i++;
                    if (directory)
                    {
                        int after = Add();
                        Epsilon[s].Add(after);
                        int run = Loop(s, Any);
                        Edges[run].Add((Literal, '/', after));
                        s = after;
                    }
                    else
                    {
                        s = Loop(s, recursive ? Any : NotSlash);
                    }
                }
                else
                {
                    s = token == '?' ? Next(s, NotSlash) : Next(s, Literal, token);
                }
            }

            Accept[s] = true;
        }

        /// <summary>
        /// One selector of the policy grammar, component by component: a component's literal characters, * any run of characters but a
        /// slash, separated by slashes; ** any number of whole components, the separators before or after it shared with its neighbours,
        /// and alone any path. Each component begins at a fresh state, so a loop that returns there never extends an earlier component.
        /// </summary>
        private void AddSelector(int start, string selector)
        {
            string[] components = selector.Split('/');
            int s = start;
            bool separator = false;
            for (int i = 0; i < components.Length; i++)
            {
                int junction = Add();
                Epsilon[s].Add(junction);
                s = junction;
                string component = components[i];
                if (component == "**")
                {
                    if (separator)
                    {
                        // (/[^/]*)*: more whole components after the ones matched so far.
                        int run = Next(s, Literal, '/');
                        Edges[run].Add((NotSlash, '\0', run));
                        Epsilon[run].Add(s);
                    }
                    else if (i == components.Length - 1)
                    {
                        // Nothing matched yet and nothing follows: any path.
                        int all = Add();
                        Epsilon[s].Add(all);
                        Edges[all].Add((Any, '\0', all));
                        Accept[all] = true;
                        return;
                    }
                    else
                    {
                        // ([^/]*/)*: whole components before the next one.
                        int run = Loop(s, NotSlash);
                        Edges[run].Add((Literal, '/', s));
                    }

                    continue;
                }

                if (separator) s = Next(s, Literal, '/');
                foreach (char c in component) s = c == '*' ? Loop(s, NotSlash) : Next(s, Literal, c);
                separator = true;
            }

            Accept[s] = true;
        }
    }

    /// <summary>A lazily determinized automaton whose subset states count toward the search's bound.</summary>
    private sealed class Lazy
    {
        private readonly Nfa nfa;
        private readonly Alphabet alphabet;
        private readonly Counter counter;
        private readonly Dictionary<int[], int> ids = new(new Sets());
        private readonly List<int[]> sets = [];
        private readonly List<bool> accepting = [];
        private readonly Dictionary<(int, int), int> steps = [];

        public Lazy(Nfa nfa, Alphabet alphabet, Counter counter)
        {
            this.nfa = nfa;
            this.alphabet = alphabet;
            this.counter = counter;
            Start = Intern(Closure([nfa.Start]));
        }

        public int Start { get; }

        public bool Accepting(int id) => accepting[id];

        public bool Empty(int id) => sets[id].Length == 0;

        public int Step(int id, int symbol)
        {
            if (steps.TryGetValue((id, symbol), out int known)) return known;
            char c = alphabet.Chars[symbol];
            var reached = new List<int>();
            foreach (int state in sets[id])
            {
                foreach ((int kind, char edge, int to) in nfa.Edges[state])
                {
                    if (Nfa.Matches(kind, edge, c)) reached.Add(to);
                }
            }

            int next = Intern(Closure(reached));
            steps[(id, symbol)] = next;
            return next;
        }

        private int[] Closure(IEnumerable<int> states)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>(states);
            while (stack.Count > 0)
            {
                int state = stack.Pop();
                if (!seen.Add(state)) continue;
                foreach (int to in nfa.Epsilon[state]) stack.Push(to);
            }

            int[] closure = [.. seen];
            Array.Sort(closure);
            return closure;
        }

        private int Intern(int[] set)
        {
            if (ids.TryGetValue(set, out int id)) return id;
            counter.Count++;
            id = sets.Count;
            ids[set] = id;
            sets.Add(set);
            accepting.Add(set.Any(s => nfa.Accept[s]));
            return id;
        }

        private sealed class Sets : IEqualityComparer<int[]>
        {
            public bool Equals(int[]? x, int[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);

            public int GetHashCode(int[] obj)
            {
                var hash = new HashCode();
                foreach (int value in obj) hash.Add(value);
                return hash.ToHashCode();
            }
        }
    }
}
