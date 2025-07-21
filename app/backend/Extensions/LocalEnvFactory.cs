// Copyright (c) Microsoft. All rights reserved.

using System.Text.RegularExpressions;

namespace MinimalApi.Extensions;

internal static class LocalEnvFactory
{
    public static async Task LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        string pattern = @"(\w+)=""([^""]+)""";
        var allLines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        foreach (var line in allLines)
        {
            Match match = Regex.Match(line, pattern);
            if (match.Groups.Count != 3)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(match.Groups[1].Value, match.Groups[2].Value);
        }
    }
}
