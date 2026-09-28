using System;
using System.Collections.Generic;

namespace JoinFS.Net
{
    static class DictionaryExtensions
    {
        public static void RemoveWhere<TKey, TValue>(this Dictionary<TKey, TValue> map, Func<TKey, TValue, bool> predicate)
        {
            List<TKey> doomed = null;
            foreach (var kv in map)
            {
                if (predicate(kv.Key, kv.Value)) (doomed ??= []).Add(kv.Key);
            }
            if (doomed != null) foreach (TKey key in doomed) map.Remove(key);
        }
    }
}
