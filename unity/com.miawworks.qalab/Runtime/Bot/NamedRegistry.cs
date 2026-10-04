// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Name → factory map behind <c>BotAdapterRegistry</c>. Names are matched ignoring case (they come
    /// from <c>-qalabAdapter</c>); registering a name again replaces it, so a game can override a
    /// built-in. Thread-safe: games may register from any static initializer.
    /// </summary>
    public sealed class NamedRegistry<T> where T : class
    {
        // "navmesh_explorer", "crimson-tactics", "ui.crawler" ok; "my game" or "" rejected (flag-friendly).
        private static readonly Regex ValidName = new Regex("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

        private readonly Dictionary<string, Func<T>> _factories =
            new Dictionary<string, Func<T>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        public void Register(string name, Func<T> factory)
        {
            if (name == null || !ValidName.IsMatch(name))
            {
                throw new ArgumentException($"bad name '{name}' (letters, digits, '_', '-', '.')", nameof(name));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            lock (_gate)
            {
                _factories[name] = factory;
            }
        }

        public bool IsRegistered(string name)
        {
            if (name == null) return false;
            lock (_gate)
            {
                return _factories.ContainsKey(name);
            }
        }

        /// <summary>A new instance, or null when nothing is registered under <paramref name="name"/>.</summary>
        public T Create(string name)
        {
            Func<T> factory;
            lock (_gate)
            {
                if (name == null || !_factories.TryGetValue(name, out factory)) return null;
            }
            return factory();
        }

        /// <summary>Registered names, sorted (for error messages and the QA Lab window).</summary>
        public IReadOnlyList<string> Names
        {
            get
            {
                List<string> names;
                lock (_gate)
                {
                    names = new List<string>(_factories.Keys);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }
    }
}
