namespace RtlFix.Core.Transforms;

/// <summary>How text is rewritten for the program that will display it.</summary>
public enum RtlMode
{
    /// <summary>Repair or Visual, decided by <see cref="RtlOptions.TargetIsBidiBlind"/>.</summary>
    Auto,

    /// <summary>
    /// Keep logical order and insert directional marks, for renderers that run the bidirectional
    /// algorithm themselves (Word, browsers, chat clients). The text stays selectable, searchable
    /// and editable; only the paragraph's direction is pinned down.
    /// </summary>
    Repair,

    /// <summary>
    /// Reorder the code points into the order they should be drawn in, for renderers that lay text
    /// out left to right whatever it contains (Notepad, terminals, game chat). The result looks
    /// right but reads backwards to anything that understands bidi.
    /// </summary>
    Visual,
}

public sealed record RtlOptions
{
    public RtlMode Mode { get; init; } = RtlMode.Auto;

    /// <summary>Set by the host when the receiving program ignores bidi altogether.</summary>
    public bool TargetIsBidiBlind { get; init; }

    /// <summary>Prefix each right-to-left paragraph with RLM so its direction stops depending on the app.</summary>
    public bool ParagraphMark { get; init; } = true;

    /// <summary>Wrap left-to-right runs in LRI…PDI so they cannot be picked apart by neutral resolution.</summary>
    public bool IsolateLeftToRightRuns { get; init; } = true;

    /// <summary>Remove directional controls already in the text before adding our own, so the transform is repeatable.</summary>
    public bool RewriteExistingControls { get; init; } = true;

    /// <summary>Convert Arabic/Persian letters to their contextual presentation forms (Visual mode).</summary>
    public bool Shape { get; init; } = true;

    /// <summary>Swap brackets and other mirrored glyphs in right-to-left runs (Visual mode, rule L4).</summary>
    public bool Mirror { get; init; } = true;

    public static readonly RtlOptions Default = new();
}
