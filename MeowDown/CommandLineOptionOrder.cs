using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

namespace MeowDown;

internal static class CommandLineOptionOrder
{
    private static readonly string[] EncodingPriorityAliases = ["--encoding-priority", "-e"];
    private static readonly string[] DfnPriorityAliases = ["--dfn-priority", "-q"];

    internal static IEnumerable<OptionResult> ByAppearance(
        IEnumerable<OptionResult> optionResults,
        IReadOnlyList<string> arguments)
    {
        //GA 下 Option.Name 含前导"--"且 Aliases 不含主名，合并后保证仅长名选项的顺序判定不退化
        return optionResults.OrderBy(result => FindFirst(arguments, OptionNames(result.Option)));
    }

    internal static string[] Arguments(OptionResult result)
    {
        var name = OptionName(result.Option);
        return result.Tokens is [{ Value.Length: > 0 } single]
            ? [$"{name}={single.Value}"]
            : [name, .. result.Tokens.Select(token => token.Value)];
    }

    internal static bool EncodingPrecedesQuality(IEnumerable<string> arguments)
    {
        var argumentArray = arguments.ToArray();
        var encodingIndex = FindFirst(argumentArray, EncodingPriorityAliases);
        var qualityIndex = FindFirst(argumentArray, DfnPriorityAliases);
        return encodingIndex < qualityIndex;
    }

    private static string OptionName(Option option)
        => option.Name.StartsWith('-') ? option.Name : "--" + option.Name;

    private static IEnumerable<string> OptionNames(Option option)
    {
        yield return OptionName(option);
        foreach (var alias in option.Aliases) yield return alias;
    }

    private static int FindFirst(IReadOnlyList<string> arguments, IEnumerable<string> aliases)
    {
        var aliasArray = aliases.ToArray();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (MatchesAnyAlias(arguments[index], aliasArray))
            {
                return index;
            }
        }
        return int.MaxValue;
    }

    private static bool MatchesAnyAlias(string argument, IReadOnlyList<string> aliases)
    {
        return aliases.Any(alias =>
            string.Equals(argument, alias, StringComparison.Ordinal)
            || argument.StartsWith(alias + "=", StringComparison.Ordinal));
    }
}
