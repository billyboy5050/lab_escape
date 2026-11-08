#nullable enable
using System.Collections;
using System.Collections.Generic;

namespace LabEscape.Core
{
    /// <summary>
    /// A map that iterates in insertion order, as a JavaScript Map or plain object does. Dictionary makes no such promise,
    /// and the order is visible in hit lists, move lists and validation messages.
    /// </summary>
    public sealed class OrderedMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : notnull
    {
        private readonly Dictionary<TKey, int> _index;
        private readonly List<KeyValuePair<TKey, TValue>> _items = new List<KeyValuePair<TKey, TValue>>();

        public OrderedMap(IEqualityComparer<TKey>? comparer = null)
        {
            _index = new Dictionary<TKey, int>(comparer);
        }

        public int Count => _items.Count;

        public void Add(TKey key, TValue value) => this[key] = value;

        /// <summary>Setting an existing key keeps its original position, as in JavaScript.</summary>
        public TValue this[TKey key]
        {
            get => _items[_index[key]].Value;
            set
            {
                if (_index.TryGetValue(key, out var at))
                {
                    _items[at] = new KeyValuePair<TKey, TValue>(key, value);
                }
                else
                {
                    _index[key] = _items.Count;
                    _items.Add(new KeyValuePair<TKey, TValue>(key, value));
                }
            }
        }

        public bool ContainsKey(TKey key) => _index.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_index.TryGetValue(key, out var at))
            {
                value = _items[at].Value;
                return true;
            }
            value = default!;
            return false;
        }

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (var pair in _items) yield return pair.Key;
            }
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                foreach (var pair in _items) yield return pair.Value;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
