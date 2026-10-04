// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Name → factory map behind <c>BotAdapterRegistry</c>. A name must pass the rule given to the
    /// constructor (for adapters, <see cref="CommandLine.IsAdapterName"/>, the same rule as
    /// <c>-qalabAdapter</c>), so every registered name can be selected from the command line.
    /// Registering a name again replaces it, so a game can override a built-in. Thread-safe: games may
    /// register from any static initializer.
    /// </summary>
    public sealed class NamedRegistry<T> where T : class
    {
        private readonly Func<string, bool> _isValidName;
        private readonly Dictionary<string, Func<T>> _factories = new Dictionary<string, Func<T>>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        public NamedRegistry(Func<string, bool> isValidName)
        {
            _isValidName = isValidName ?? throw new ArgumentNullException(nameof(isValidName));
        }

        public void Register(string name, Func<T> factory)
        {
            if (name == null || !_isValidName(name))
            {
                throw new ArgumentException($"bad name '{name}'", nameof(name));
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
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }
    }
}
