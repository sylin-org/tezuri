using System.Text;
using System.Text.RegularExpressions;

namespace Tezuri.Infrastructure.Git;

internal static class GitAllowedPathMatcher
{
    public static bool IsMatch(string pattern, string path)
    {
        var patternSegments = pattern.Split('/');
        var pathSegments = path.Split('/');
        var states = new Dictionary<(int Pattern, int Path), bool>();
        return Match(patternSegments, pathSegments, 0, 0, states);
    }

    private static bool Match(
        IReadOnlyList<string> pattern,
        IReadOnlyList<string> path,
        int patternIndex,
        int pathIndex,
        IDictionary<(int Pattern, int Path), bool> states)
    {
        var key = (patternIndex, pathIndex);
        if (states.TryGetValue(key, out var cached))
        {
            return cached;
        }

        bool result;
        if (patternIndex == pattern.Count)
        {
            result = pathIndex == path.Count;
        }
        else if (pattern[patternIndex] == "**")
        {
            result = Match(pattern, path, patternIndex + 1, pathIndex, states) ||
                     (pathIndex < path.Count &&
                      Match(pattern, path, patternIndex, pathIndex + 1, states));
        }
        else
        {
            result = pathIndex < path.Count &&
                     SegmentMatches(pattern[patternIndex], path[pathIndex]) &&
                     Match(pattern, path, patternIndex + 1, pathIndex + 1, states);
        }

        states[key] = result;
        return result;
    }

    private static bool SegmentMatches(string pattern, string value)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            switch (pattern[index])
            {
                case '*':
                    expression.Append(".*");
                    break;
                case '?':
                    expression.Append('.');
                    break;
                case '[':
                    var close = pattern.IndexOf(']', index + 1);
                    if (close < 0)
                    {
                        expression.Append("\\[");
                        break;
                    }

                    var members = pattern[(index + 1)..close];
                    if (members.Length == 0)
                    {
                        expression.Append("\\[\\]");
                    }
                    else
                    {
                        expression.Append('[');
                        foreach (var member in members.Where(member => member != '-'))
                        {
                            expression.Append(Regex.Escape(member.ToString()));
                        }

                        if (members.Contains('-'))
                        {
                            expression.Append("\\-");
                        }

                        expression.Append(']');
                    }

                    index = close;
                    break;
                default:
                    expression.Append(Regex.Escape(pattern[index].ToString()));
                    break;
            }
        }

        expression.Append('$');
        var options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.IsMatch(
            value,
            expression.ToString(),
            options,
            TimeSpan.FromMilliseconds(100));
    }
}
