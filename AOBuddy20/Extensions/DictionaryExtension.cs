// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: DictionaryExtension.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

namespace AOBuddy20.Extensions;

public static class DictionaryExtension
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> valueFactory) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var value))
        {
            value = valueFactory(key);
            dict[key] = value;
        }

        return value;
    }
}