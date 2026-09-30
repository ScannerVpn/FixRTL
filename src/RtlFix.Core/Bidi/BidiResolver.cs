using System.Text;
using RtlFix.Core.Unicode;

namespace RtlFix.Core.Bidi;

/// <summary>
/// A managed implementation of the Unicode Bidirectional Algorithm (UAX #9, section 3.3).
/// </summary>
/// <remarks>
/// The rules run in the order the spec states them, so a wrong result can be traced back to one
/// rule: X1-X8 assign the explicit levels, X9 and BD13 carve the paragraph into isolating run
/// sequences, W1-W7 plus N0 resolve implicit types inside each sequence, I1-I2 raise the levels,
/// and L1 finishes the line. Reordering (L2) lives in <see cref="BidiAnalysis.VisualIndices"/>,
/// since only the visual output needs it.
/// </remarks>
public static class BidiResolver
{
    const byte MaxDepth = 125;
    const int MaxBracketStack = 63;

    enum Override
    {
        None,
        Ltr,
        Rtl,
    }

    readonly struct StackEntry(byte level, Override @override, bool isolate)
    {
        public byte Level { get; } = level;
        public Override Override { get; } = @override;
        public bool Isolate { get; } = isolate;
    }

    /// <param name="runes">One paragraph. Rules are scoped per paragraph, so type B must not appear.</param>
    /// <param name="paragraphLevel">0 for a left-to-right paragraph, 1 for a right-to-left one.</param>
    public static BidiAnalysis Resolve(Rune[] runes, byte paragraphLevel)
    {
        var n = runes.Length;
        var original = new BidiClass[n];
        var types = new BidiClass[n];
        var levels = new byte[n];
        var removed = new bool[n];
        var pdiOf = new int[n];
        var initiatorOf = new int[n];
        Array.Fill(pdiOf, -1);
        Array.Fill(initiatorOf, -1);

        for (var i = 0; i < n; i++)
        {
            original[i] = types[i] = UnicodeTables.GetBidiClass(runes[i]);
            levels[i] = paragraphLevel;
        }

        ApplyExplicitLevels(types, levels, removed, pdiOf, initiatorOf, paragraphLevel);

        for (var i = 0; i < n; i++)
            removed[i] = types[i] is BidiClass.BN
                or BidiClass.LRE or BidiClass.LRO or BidiClass.RLE or BidiClass.RLO or BidiClass.PDF;

        ApplyImplicitLevels(runes, types, levels, removed, pdiOf, initiatorOf, paragraphLevel);
        ApplyLineResets(original, levels, removed, paragraphLevel);

        return new BidiAnalysis
        {
            Runes = runes,
            Levels = levels,
            Types = original,
            Removed = removed,
            ParagraphLevel = paragraphLevel,
        };
    }

    /// <summary>Rules X1 through X8: explicit embeddings, overrides, isolates, and their levels.</summary>
    static void ApplyExplicitLevels(
        BidiClass[] types, byte[] levels, bool[] removed, int[] pdiOf, int[] initiatorOf, byte paragraphLevel)
    {
        var stack = new List<StackEntry> { new(paragraphLevel, Override.None, false) };
        var openIsolates = new List<int>();
        var overflowIsolate = 0;
        var overflowEmbedding = 0;
        var validIsolate = 0;

        void Push(byte level, Override @override, bool isolate) =>
            stack.Add(new StackEntry(level, @override, isolate));

        void Pop()
        {
            stack.RemoveAt(stack.Count - 1);
        }

        // X2/X3: the least odd (embedding) or even (override) level above the current one.
        byte NextLevel(bool odd)
        {
            var level = stack[^1].Level;
            return (byte)((level & 1) == (odd ? 1 : 0) ? level + 2 : level + 1);
        }

        void ApplyOverride(int index)
        {
            switch (stack[^1].Override)
            {
                case Override.Rtl: types[index] = BidiClass.R; break;
                case Override.Ltr: types[index] = BidiClass.L; break;
            }
        }

        for (var i = 0; i < types.Length; i++)
        {
            switch (types[i])
            {
                case BidiClass.RLE:
                case BidiClass.LRE:
                case BidiClass.RLO:
                case BidiClass.LRO:
                {
                    levels[i] = stack[^1].Level;
                    var (odd, @override) = types[i] switch
                    {
                        BidiClass.RLE => (true, Override.None),
                        BidiClass.LRE => (false, Override.None),
                        BidiClass.RLO => (true, Override.Rtl),
                        _ => (false, Override.Ltr),
                    };
                    var next = NextLevel(odd);
                    if (next <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                        Push(next, @override, false);
                    else if (overflowIsolate == 0)
                        overflowEmbedding++;
                    removed[i] = true;
                    break;
                }

                case BidiClass.PDF:
                {
                    // X7
                    levels[i] = stack[^1].Level;
                    if (overflowIsolate > 0)
                    {
                        // Inside an overflow isolate: there is nothing to terminate.
                    }
                    else if (overflowEmbedding > 0)
                    {
                        overflowEmbedding--;
                    }
                    else if (!stack[^1].Isolate && stack.Count >= 2)
                    {
                        Pop();
                        levels[i] = stack[^1].Level;
                    }
                    removed[i] = true;
                    break;
                }

                case BidiClass.RLI:
                case BidiClass.LRI:
                case BidiClass.FSI:
                {
                    // X5a / X5b / X5c
                    levels[i] = stack[^1].Level;
                    ApplyOverride(i);
                    var rtl = types[i] == BidiClass.RLI
                        || (types[i] == BidiClass.FSI && DecideIsolateDirection(types, i));
                    var next = NextLevel(rtl);
                    if (next <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                    {
                        validIsolate++;
                        Push(next, Override.None, true);
                        openIsolates.Add(i);
                    }
                    else
                    {
                        overflowIsolate++;
                    }
                    break;
                }

                case BidiClass.PDI:
                {
                    // X6a
                    if (overflowIsolate > 0)
                    {
                        overflowIsolate--;
                    }
                    else if (validIsolate > 0)
                    {
                        overflowEmbedding = 0;
                        while (stack.Count > 1 && !stack[^1].Isolate) stack.RemoveAt(stack.Count - 1);
                        if (stack.Count > 1) stack.RemoveAt(stack.Count - 1);
                        validIsolate--;
                        var open = openIsolates[^1];
                        openIsolates.RemoveAt(openIsolates.Count - 1);
                        pdiOf[open] = i;
                        initiatorOf[i] = open;
                    }
                    levels[i] = stack[^1].Level;
                    ApplyOverride(i);
                    break;
                }

                case BidiClass.B:
                    levels[i] = paragraphLevel;
                    break;

                default:
                    // X6 (and BN, which X9 removes anyway)
                    levels[i] = stack[^1].Level;
                    if (types[i] != BidiClass.BN) ApplyOverride(i);
                    break;
            }
        }
    }

    /// <summary>
    /// X5c: run P2/P3 over the FSI's own contents as if they were a paragraph, skipping any nested
    /// isolate. True means that yields embedding level 1, so the FSI behaves as an RLI.
    /// </summary>
    static bool DecideIsolateDirection(BidiClass[] types, int start)
    {
        var depth = 0;
        for (var i = start + 1; i < types.Length; i++)
        {
            switch (types[i])
            {
                case BidiClass.RLI:
                case BidiClass.LRI:
                case BidiClass.FSI:
                    depth++;
                    break;
                case BidiClass.PDI:
                    if (depth == 0) return false;
                    depth--;
                    break;
                case BidiClass.B:
                    return false;
                default:
                    if (depth != 0) break;
                    if (types[i] is BidiClass.R or BidiClass.AL) return true;
                    if (types[i] == BidiClass.L) return false;
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// X9 and X10, then W1-W7, N0 and I1-I2, applied per isolating run sequence.
    /// </summary>
    static void ApplyImplicitLevels(
        Rune[] runes, BidiClass[] types, byte[] levels, bool[] removed, int[] pdiOf, int[] initiatorOf,
        byte paragraphLevel)
    {
        var visible = new List<int>();
        for (var i = 0; i < runes.Length; i++)
            if (!removed[i])
                visible.Add(i);
        if (visible.Count == 0) return;

        var explicitLevels = (byte[])levels.Clone();

        // Level runs over the visible characters, then grouped into sequences (BD13).
        var runs = new List<(int Start, int End)>();
        var runOfChar = new int[runes.Length];
        for (var k = 0; k < visible.Count;)
        {
            var first = visible[k];
            var level = explicitLevels[first];
            var end = k + 1;
            while (end < visible.Count && explicitLevels[visible[end]] == level) end++;
            var index = runs.Count;
            for (var m = k; m < end; m++) runOfChar[visible[m]] = index;
            runs.Add((first, visible[end - 1]));
            k = end;
        }

        var visibleArray = visible.ToArray();
        var positionOf = new int[runes.Length];
        for (var k = 0; k < visibleArray.Length; k++) positionOf[visibleArray[k]] = k;

        var used = new bool[runs.Count];
        for (var r = 0; r < runs.Count; r++)
        {
            var runStart = runs[r].Start;
            if (types[runStart] == BidiClass.PDI && initiatorOf[runStart] >= 0) continue;
            if (used[r]) continue;

            var sequence = new List<int>();
            used[r] = true;
            AddRun(runs[r], sequence, removed);

            while (true)
            {
                var last = sequence[^1];
                if (!UnicodeTables.IsIsolateInitiator(types[last])) break;
                var match = pdiOf[last];
                if (match < 0) break;
                var next = runOfChar[match];
                if (used[next]) break;
                used[next] = true;
                AddRun(runs[next], sequence, removed);
            }

            ResolveSequence(runes, types, explicitLevels, levels, paragraphLevel, sequence, visibleArray,
                positionOf, pdiOf);
        }

        static void AddRun((int Start, int End) run, List<int> sequence, bool[] removed)
        {
            for (var i = run.Start; i <= run.End; i++)
                if (!removed[i])
                    sequence.Add(i);
        }
    }

    static void ResolveSequence(
        Rune[] runes, BidiClass[] types, byte[] explicitLevels, byte[] levels, byte paragraphLevel,
        List<int> sequence, int[] visible, int[] positionOf, int[] pdiOf)
    {
        var first = sequence[0];
        var last = sequence[^1];

        // X10: sos / eos come from the higher of the two levels meeting at each sequence boundary.
        var position = positionOf[first];
        var before = position > 0 ? explicitLevels[visible[position - 1]] : paragraphLevel;
        var sos = Math.Max(before, explicitLevels[first]) % 2 == 1 ? BidiClass.R : BidiClass.L;

        var lastPosition = positionOf[last];
        var unclosed = UnicodeTables.IsIsolateInitiator(types[last]) && pdiOf[last] < 0;
        var after = lastPosition + 1 < visible.Length && !unclosed
            ? explicitLevels[visible[lastPosition + 1]]
            : paragraphLevel;
        var eos = Math.Max(after, explicitLevels[last]) % 2 == 1 ? BidiClass.R : BidiClass.L;

        var working = new BidiClass[sequence.Count];
        for (var k = 0; k < sequence.Count; k++) working[k] = types[sequence[k]];

        // W1: a mark follows its base, unless that base is an isolate boundary.
        for (var k = 0; k < working.Length; k++)
        {
            if (working[k] != BidiClass.NSM) continue;
            if (k == 0)
            {
                working[k] = sos;
                continue;
            }
            var previous = types[sequence[k - 1]];
            working[k] = UnicodeTables.IsIsolateInitiator(previous) || previous == BidiClass.PDI
                ? BidiClass.ON
                : working[k - 1];
        }

        // W2: European numbers after an Arabic letter become Arabic numbers.
        for (var k = 0; k < working.Length; k++)
            if (working[k] == BidiClass.EN && StrongBefore(working, k, sos, BidiClass.AL) == BidiClass.AL)
                working[k] = BidiClass.AN;

        // W3
        for (var k = 0; k < working.Length; k++)
            if (working[k] == BidiClass.AL) working[k] = BidiClass.R;

        // W4: one separator between two numbers of the same kind joins them.
        for (var k = 1; k + 1 < working.Length; k++)
        {
            if (working[k] == BidiClass.ES && working[k - 1] == BidiClass.EN && working[k + 1] == BidiClass.EN)
                working[k] = BidiClass.EN;
            else if (working[k] == BidiClass.CS && working[k - 1] == working[k + 1]
                     && working[k - 1] is BidiClass.EN or BidiClass.AN)
                working[k] = working[k - 1];
        }

        // W5: European terminators touching a European number become numbers.
        for (var k = 0; k < working.Length; k++)
        {
            if (working[k] != BidiClass.ET) continue;
            var start = k;
            while (start > 0 && working[start - 1] == BidiClass.ET) start--;
            var end = k;
            while (end + 1 < working.Length && working[end + 1] == BidiClass.ET) end++;
            if (start > 0 && working[start - 1] == BidiClass.EN)
                for (var m = start; m <= end; m++) working[m] = BidiClass.EN;
            else if (end + 1 < working.Length && working[end + 1] == BidiClass.EN)
                for (var m = start; m <= end; m++) working[m] = BidiClass.EN;
            k = end;
        }

        // W6
        for (var k = 0; k < working.Length; k++)
            if (working[k] is BidiClass.ES or BidiClass.ET or BidiClass.CS) working[k] = BidiClass.ON;

        // W7: a European number whose first strong predecessor is L turns left-to-right.
        for (var k = 0; k < working.Length; k++)
            if (working[k] == BidiClass.EN && StrongBefore(working, k, sos) == BidiClass.L)
                working[k] = BidiClass.L;

        // N0
        ApplyBrackets(runes, types, sequence, working, explicitLevels, sos);

        // N1 / N2
        ApplyNeutrals(sequence, working, explicitLevels, sos, eos);

        // I1 / I2
        for (var k = 0; k < sequence.Count; k++)
        {
            var level = explicitLevels[sequence[k]];
            levels[sequence[k]] = ((level & 1) == 0, working[k]) switch
            {
                (true, BidiClass.R) => (byte)(level + 1),
                (true, BidiClass.EN or BidiClass.AN) => (byte)(level + 2),
                (false, BidiClass.L or BidiClass.EN or BidiClass.AN) => (byte)(level + 1),
                _ => level,
            };
        }
    }

    static BidiClass StrongBefore(BidiClass[] working, int k, BidiClass sos, params BidiClass[] extra)
    {
        for (var m = k - 1; m >= 0; m--)
            if (working[m] is BidiClass.L or BidiClass.R || Array.IndexOf(extra, working[m]) >= 0)
                return working[m];
        return sos;
    }

    /// <summary>
    /// N0 with BD14-BD16: a bracket pair takes the direction of the strong types it encloses, or the
    /// established context in front of it when those point the other way.
    /// </summary>
    static void ApplyBrackets(
        Rune[] runes, BidiClass[] explicitTypes, List<int> sequence, BidiClass[] working, byte[] explicitLevels,
        BidiClass sos)
    {
        var stack = new List<(int Paired, int Position)>(MaxBracketStack);
        var pairs = new List<(int Open, int Close)>();
        for (var k = 0; k < sequence.Count; k++)
        {
            if (working[k] != BidiClass.ON) continue;
            var rune = runes[sequence[k]];
            switch (UnicodeTables.GetBracketType(rune))
            {
                case BracketType.Open:
                    if (stack.Count >= MaxBracketStack)
                    {
                        stack.Clear();
                        pairs.Clear();
                        return;
                    }
                    stack.Add((UnicodeTables.GetPairedBracket(rune), k));
                    break;
                case BracketType.Close when stack.Count > 0:
                    // The stack holds the closer each pending opener expects (BD16). U+2329/U+232A
                    // are canonically equivalent to U+3008/U+3009, so both spellings match.
                    for (var m = stack.Count - 1; m >= 0; m--)
                    {
                        if (!CloserMatches(stack[m].Paired, runes[sequence[k]].Value)) continue;
                        pairs.Add((stack[m].Position, k));
                        stack.RemoveRange(m, stack.Count - m);
                        break;
                    }
                    break;
            }
        }

        // N0 walks the pairs in the order their opening brackets appear, which for nested pairs is
        // not the order BD16 discovered them.
        pairs.Sort((a, b) => a.Open.CompareTo(b.Open));
        var changed = new List<int>();
        foreach (var (open, close) in pairs)
        {
            var embedding = (explicitLevels[sequence[open]] & 1) == 1 ? BidiClass.R : BidiClass.L;

            var inside = BidiClass.ON;
            for (var k = open + 1; k < close; k++)
            {
                var t = AsStrong(working[k]);
                if (t == BidiClass.ON) continue;
                inside = t;
                if (t == embedding) break;
            }

            if (inside == BidiClass.ON)
            {
                // Nothing strong inside: the pair is left for rules N1/N2.
                continue;
            }

            if (inside == embedding) working[open] = working[close] = embedding;
            else
            {
                // Opposite types inside, so only an equally opposite context in front holds them there.
                var context = ContextBefore(working, open, sos);
                working[open] = working[close] = context == embedding ? embedding : context;
            }
            changed.Add(open);
            changed.Add(close);
        }

        // Marks that followed a bracket N0 renamed now follow it into the new type.
        foreach (var k in changed)
            for (var m = k + 1; m < working.Length && explicitTypes[sequence[m]] == BidiClass.NSM; m++)
                working[m] = working[k];

        static BidiClass AsStrong(BidiClass t) => t switch
        {
            BidiClass.EN or BidiClass.AN => BidiClass.R,
            BidiClass.L or BidiClass.R => t,
            _ => BidiClass.ON,
        };
    }

    /// <summary>
    /// N1 and N2: a run of neutrals joins the surrounding direction when both sides agree, and
    /// falls back to its own embedding direction otherwise.
    /// </summary>
    static void ApplyNeutrals(
        List<int> sequence, BidiClass[] working, byte[] explicitLevels, BidiClass sos, BidiClass eos)
    {
        for (var k = 0; k < working.Length;)
        {
            if (!IsNeutral(working[k]))
            {
                k++;
                continue;
            }
            var start = k;
            while (k < working.Length && IsNeutral(working[k])) k++;

            var left = start == 0 ? sos : AsInfluence(working[start - 1]);
            var right = k == working.Length ? eos : AsInfluence(working[k]);
            var target = left == right && left != BidiClass.ON
                ? left
                : ((explicitLevels[sequence[start]] & 1) == 1 ? BidiClass.R : BidiClass.L);
            for (var m = start; m < k; m++) working[m] = target;
        }

        static bool IsNeutral(BidiClass c) => c is BidiClass.ON or BidiClass.WS
            or BidiClass.B or BidiClass.S
            or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI;

        static BidiClass AsInfluence(BidiClass c) => c switch
        {
            BidiClass.EN or BidiClass.AN or BidiClass.R or BidiClass.AL => BidiClass.R,
            BidiClass.L => BidiClass.L,
            _ => BidiClass.ON,
        };
    }

    /// <summary>The first strong type in front of an opening bracket, or sos when there is none.</summary>
    static BidiClass ContextBefore(BidiClass[] working, int open, BidiClass sos)
    {
        for (var m = open - 1; m >= 0; m--)
        {
            var t = working[m] switch
            {
                BidiClass.EN or BidiClass.AN => BidiClass.R,
                BidiClass.L or BidiClass.R => working[m],
                _ => BidiClass.ON,
            };
            if (t != BidiClass.ON) return t;
        }
        return sos;
    }

    static bool CloserMatches(int expected, int actual) =>
        expected == actual
        || (expected, actual) is (0x3009, 0x232A) or (0x232A, 0x3009);

    /// <summary>Rule L1: separators, and the whitespace in front of them, sit at the paragraph level.</summary>
    static void ApplyLineResets(BidiClass[] original, byte[] levels, bool[] removed, byte paragraphLevel)
    {
        var n = levels.Length;
        for (var i = 0; i < n; i++)
        {
            if (removed[i] || !IsResettable(original[i])) continue;
            levels[i] = paragraphLevel;
            for (var j = i - 1; j >= 0 && !removed[j] && original[j] == BidiClass.WS; j--)
                levels[j] = paragraphLevel;
        }

        var end = n - 1;
        while (end >= 0 && (removed[end] || IsResettable(original[end]) || IsTrailing(original[end]))) end--;
        for (var i = n - 1; i > end; i--)
            if (!removed[i] && (IsResettable(original[i]) || IsTrailing(original[i])))
                levels[i] = paragraphLevel;

        static bool IsResettable(BidiClass c) => c is BidiClass.B or BidiClass.S;
        static bool IsTrailing(BidiClass c) => c is BidiClass.WS
            or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI;
    }
}
