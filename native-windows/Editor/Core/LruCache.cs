using System;
using System.Collections.Generic;

namespace FamidashEditor.Editor.Core
{
    public class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
        private readonly LinkedList<KeyValuePair<TKey, TValue>> list = new LinkedList<KeyValuePair<TKey, TValue>>();

        public LruCache(int capacity)
        {
            this.capacity = Math.Max(16, capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            if (map.TryGetValue(key, out var node))
            {
                // move to front
                list.Remove(node);
                list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        public TValue? this[TKey key]
        {
            set { Put(key, value); }
            get
            {
                if (TryGetValue(key, out var v)) return v;
                return default;
            }
        }

        public void Put(TKey key, TValue? value)
        {
            if (map.TryGetValue(key, out var node))
            {
                node.Value = new KeyValuePair<TKey, TValue>(key, value!);
                list.Remove(node);
                list.AddFirst(node);
            }
            else
            {
                var kv = new KeyValuePair<TKey, TValue>(key, value!);
                var n = list.AddFirst(kv);
                map[key] = n;
                if (map.Count > capacity)
                {
                    var last = list.Last;
                    if (last != null)
                    {
                        map.Remove(last.Value.Key);
                        list.RemoveLast();
                    }
                }
            }
        }

        public void Clear()
        {
            map.Clear();
            list.Clear();
        }
    }
}
