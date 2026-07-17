using System;

namespace MdEditor.Services;

/// <summary>
/// Pure text-matching primitives used by the Find/Replace dialog. Tab orchestration
/// (which tab is active, switching tabs, applying selections) lives in the ViewModel layer.
/// </summary>
public class SearchService
{
    /// <summary>
    /// Finds the next occurrence of <paramref name="query"/> at or after <paramref name="fromIndex"/>,
    /// without wrapping. Returns -1 if not found between fromIndex and the end of the text.
    /// </summary>
    public int IndexOf(string text, string query, int fromIndex, bool matchCase)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return -1;
        }

        fromIndex = Math.Clamp(fromIndex, 0, text.Length);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return text.IndexOf(query, fromIndex, comparison);
    }

    /// <summary>
    /// Finds the next occurrence of <paramref name="query"/> at or after <paramref name="fromIndex"/>,
    /// wrapping around to the start of the text if nothing is found after it.
    /// Returns -1 if the query does not occur anywhere in the text.
    /// </summary>
    public int FindNext(string text, string query, int fromIndex, bool matchCase)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return -1;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        fromIndex = Math.Clamp(fromIndex, 0, text.Length);

        var idx = text.IndexOf(query, fromIndex, comparison);
        if (idx < 0)
        {
            idx = text.IndexOf(query, 0, comparison);
        }

        return idx;
    }

    public string ReplaceAll(string text, string query, string replacement, bool matchCase, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new System.Text.StringBuilder();
        var pos = 0;
        while (true)
        {
            var idx = text.IndexOf(query, pos, comparison);
            if (idx < 0)
            {
                result.Append(text, pos, text.Length - pos);
                break;
            }

            result.Append(text, pos, idx - pos);
            result.Append(replacement);
            pos = idx + query.Length;
            count++;
        }

        return count > 0 ? result.ToString() : text;
    }
}
