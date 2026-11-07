namespace Haulout.Sim.Play;

internal enum ResolutionKind { Found, Ambiguous, Unknown }

internal sealed record Resolution<T>(ResolutionKind Kind, T? Match, IReadOnlyList<T> Candidates) where T : class;

/// <summary>Shared by all adapters: case-insensitive, trimmed, leading "the" ignored; exact beats substring.</summary>
internal static class NameResolver
{
    public static string Normalize(string? input)
    {
        var s = string.Join(' ', (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (s.StartsWith("the ", StringComparison.Ordinal)) s = s[4..].TrimStart();
        return s;
    }

    public static Resolution<T> Resolve<T>(
        IEnumerable<T> items, string? input,
        Func<T, string> id, Func<T, string> name, Func<T, IReadOnlyList<string>> aliases) where T : class
    {
        var q = Normalize(input);
        var list = items.ToList();
        if (q.Length == 0) return new(ResolutionKind.Unknown, null, []);

        IEnumerable<string> Keys(T t) => aliases(t).Append(name(t)).Append(id(t)).Select(Normalize);

        var exact = list.Where(t => Keys(t).Any(k => k == q)).ToList();
        if (exact.Count == 1) return new(ResolutionKind.Found, exact[0], exact);
        if (exact.Count > 1) return new(ResolutionKind.Ambiguous, null, exact);

        var partial = list.Where(t => Keys(t).Any(k => k.Contains(q, StringComparison.Ordinal))).ToList();
        if (partial.Count == 1) return new(ResolutionKind.Found, partial[0], partial);
        if (partial.Count > 1) return new(ResolutionKind.Ambiguous, null, partial);
        return new(ResolutionKind.Unknown, null, []);
    }
}
