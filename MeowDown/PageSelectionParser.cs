using System;
using System.Collections.Generic;

namespace MeowDown;

internal static class PageSelectionParser
{
    private static readonly HashSet<string> LastPageAliases =
        new(StringComparer.OrdinalIgnoreCase) { "LAST", "NEW", "LATEST" };

    /// <summary>
    /// 仅校验分P选择语法（页码、范围、逗号列表、LAST/LATEST/NEW 别名与 ALL），
    /// 供任务提交期使用；页码是否越界需分P总数，仍由执行期 <see cref="Parse"/> 判定。
    /// 语法非法时返回描述文本，合法（含留空=自动选择）返回 null。
    /// </summary>
    internal static string? ValidateSyntax(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return null;
        }

        foreach (var rawToken in selection.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                return InvalidSyntax(selection);
            }
            if (LastPageAliases.Contains(token))
            {
                continue;
            }
            if (token.Contains('-'))
            {
                var range = token.Split('-', StringSplitOptions.TrimEntries);
                if (range.Length != 2
                    || !TryParsePageNumber(range[0], out var start)
                    || !TryParsePageNumber(range[1], out var end)
                    || start > end)
                {
                    return InvalidSyntax(selection);
                }
                continue;
            }
            if (!TryParsePageNumber(token, out _))
            {
                return InvalidSyntax(selection);
            }
        }
        return null;
    }

    private static string InvalidSyntax(string selection)
        => $"分P参数无效: {selection}。请输入页码、范围（如 5-8）、逗号列表、LAST/LATEST/NEW 或 ALL。";

    internal static List<string>? Parse(string selection, int pageCount)
    {
        if (pageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }

        var normalized = selection.Trim();
        if (normalized.Length == 0)
        {
            throw InvalidSelection(selection, pageCount);
        }
        if (string.Equals(normalized, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var selectedPages = new List<string>();
        var seenPages = new HashSet<int>();
        foreach (var rawToken in normalized.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                throw InvalidSelection(selection, pageCount);
            }

            if (LastPageAliases.Contains(token))
            {
                AddPage(pageCount, pageCount, selectedPages, seenPages, selection);
                continue;
            }

            if (token.Contains('-'))
            {
                var range = token.Split('-', StringSplitOptions.TrimEntries);
                if (range.Length != 2
                    || !TryParsePageNumber(range[0], out var start)
                    || !TryParsePageNumber(range[1], out var end)
                    || start > end)
                {
                    throw InvalidSelection(selection, pageCount);
                }
                for (var page = start; page <= end; page++)
                {
                    AddPage(page, pageCount, selectedPages, seenPages, selection);
                }
                continue;
            }

            if (!TryParsePageNumber(token, out var selectedPage))
            {
                throw InvalidSelection(selection, pageCount);
            }
            AddPage(selectedPage, pageCount, selectedPages, seenPages, selection);
        }

        return selectedPages;
    }

    private static bool TryParsePageNumber(string value, out int page)
    {
        page = 0;
        if (value.Length == 0)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }
        return int.TryParse(value, out page);
    }

    private static void AddPage(
        int page,
        int pageCount,
        List<string> selectedPages,
        HashSet<int> seenPages,
        string selection)
    {
        if (page < 1 || page > pageCount)
        {
            throw InvalidSelection(selection, pageCount);
        }
        if (seenPages.Add(page))
        {
            selectedPages.Add(page.ToString());
        }
    }

    private static ArgumentException InvalidSelection(string selection, int pageCount)
    {
        return new ArgumentException(
            $"分P参数无效: {selection}。请输入 1-{pageCount}、范围、逗号列表、LAST/LATEST/NEW 或 ALL。",
            nameof(selection));
    }
}
