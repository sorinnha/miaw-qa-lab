using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Ground-truth recorder for benchmark runs (<c>-qalabBenchmark</c>). Seeded scripts call
    /// <see cref="Trigger"/> when their bug happens; at the end of the run <c>labels.json</c> lists each
    /// triggered seed with its catalog entry and trigger times. Outside benchmark mode every call is a
    /// cheap no-op. Labels never go into <c>events.jsonl</c> (triage must not see them).
    /// </summary>
    public static class LabelRecorder
    {
        private static readonly List<SeedCatalogEntry> CatalogEntries = new List<SeedCatalogEntry>();
        private static volatile LabelBook _book;
        private static IClock _clock;
        private static IMainThreadState _state;

        /// <summary>The game's seed catalog. Register it at startup (before the first scene loads).</summary>
        public static void UseCatalog(IEnumerable<SeedCatalogEntry> entries)
        {
            CatalogEntries.Clear();
            CatalogEntries.AddRange(entries);
        }

        public static IReadOnlyList<SeedCatalogEntry> Catalog => CatalogEntries;

        /// <summary>A seeded bug just happened. Safe from any thread.</summary>
        public static void Trigger(string bugId)
        {
            var book = _book;
            if (book == null)
            {
                return;
            }
            book.Trigger(bugId, _clock.Seconds, _state.Scene);
        }

        internal static void Begin(bool benchmark, IClock clock, IMainThreadState state)
        {
            _clock = clock;
            _state = state;
            _book = benchmark ? new LabelBook(CatalogEntries) : null;
        }

        /// <summary>Write labels.json (benchmark only) and stop recording. Returns unknown ids, if any.</summary>
        internal static IReadOnlyCollection<string> End(string labelsPath, string runId)
        {
            var book = _book;
            _book = null;
            if (book == null)
            {
                return new List<string>();
            }
            book.WriteTo(labelsPath, runId);
            return book.UnknownTriggers;
        }

        internal static void Reset()
        {
            _book = null;
            _clock = null;
            _state = null;
        }
    }
}
