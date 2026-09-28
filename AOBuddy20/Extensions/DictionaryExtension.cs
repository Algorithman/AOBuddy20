// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: DictionaryExtension.cs
// 
// Last modified: 2026-09-28 17:09
// Created:       2026-09-28 17:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

namespace AOBuddy20.Extensions;

public static class DictionaryExtension
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> valueFactory)
    {
        if (!dict.TryGetValue(key, out TValue value))
        {
            value = valueFactory(key);
            dict[key] = value;
        }
        return value;
    }    
}