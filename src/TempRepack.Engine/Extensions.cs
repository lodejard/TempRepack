using System;
using System.Collections.Generic;

namespace TempRepack.Engine
{
    public static class Extensions
    {
        public static V Get<K, V>(this IDictionary<K, V> dictionary, K key)
        {
            V result;
            if (!dictionary.TryGetValue(key, out result))
            {
                return default(V);
            }
            return result;
        }

        public static V GetOrAdd<K, V>(this IDictionary<K, V> dictionary, K key, Func<V> value)
        {
            V result;
            if (!dictionary.TryGetValue(key, out result))
            {
                return dictionary[key] = value();
            }
            return result;
        }
    }
}