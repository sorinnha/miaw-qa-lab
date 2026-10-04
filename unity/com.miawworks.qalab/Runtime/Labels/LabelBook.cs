// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Collects seed triggers during a benchmark run and builds <c>labels.json</c> (qalab.labels/1).
    /// Only seeds that fired at least once are listed: a seed that was enabled but never reached is
    /// not ground truth for that run. Every screenshot is listed with the visual labels that were true
    /// when it was taken (an empty list = a clean frame). Nothing here ever goes into <c>events.jsonl</c>.
    /// Thread-safe: seeded scripts may trigger from any thread.
    /// </summary>
    public sealed class LabelBook
    {
        public const string SchemaId = "qalab.labels/1";

        private readonly Dictionary<string, SeedCatalogEntry> _catalog = new Dictionary<string, SeedCatalogEntry>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, List<JObject>> _triggers = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
        private readonly HashSet<string> _unknown = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<JObject> _screenshots = new List<JObject>();
        private readonly object _lock = new object();

        public LabelBook(IEnumerable<SeedCatalogEntry> catalog)
        {
            foreach (var entry in catalog ?? Array.Empty<SeedCatalogEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.BugId))
                {
                    continue;
                }
                if (_catalog.ContainsKey(entry.BugId))
                {
                    throw new ArgumentException($"duplicate catalog entry {entry.BugId}");
                }
                _catalog.Add(entry.BugId, entry);
            }
        }

        public bool IsKnown(string bugId) => bugId != null && _catalog.ContainsKey(bugId);

        /// <summary>Ids that were triggered but have no catalog entry (left out of labels.json).</summary>
        public IReadOnlyCollection<string> UnknownTriggers
        {
            get { lock (_lock) { return new List<string>(_unknown); } }
        }

        /// <summary>Record that a seed fired at run time <paramref name="t"/> in <paramref name="scene"/>.</summary>
        public void Trigger(string bugId, double t, string scene)
        {
            lock (_lock)
            {
                if (!IsKnown(bugId))
                {
                    _unknown.Add(bugId ?? "(null)");
                    return;
                }
                if (!_triggers.TryGetValue(bugId, out var list))
                {
                    list = new List<JObject>();
                    _triggers.Add(bugId, list);
                    _order.Add(bugId);
                }
                var trigger = new JObject { ["t"] = Math.Round(t, 3) };
                if (!string.IsNullOrEmpty(scene))
                {
                    trigger["scene"] = scene;
                }
                list.Add(trigger);
            }
        }

        /// <summary>
        /// Record a screenshot's ground truth: the run-relative <paramref name="path"/>, run time
        /// <paramref name="t"/>, the visual <paramref name="labels"/> visible in it and the seeds that caused
        /// them. A visual seed seen in a shot also counts as triggered at <paramref name="t"/>.
        /// </summary>
        public void AddScreenshot(string path, double t, IEnumerable<string> labels, IEnumerable<string> bugIds, string scene)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            var labelSet = new List<string>();
            foreach (var label in labels ?? Array.Empty<string>())
            {
                if (!VisualLabels.IsKnown(label)) throw new ArgumentException($"unknown visual label '{label}'", nameof(labels));
                if (!labelSet.Contains(label)) labelSet.Add(label);
            }
            labelSet.Sort(StringComparer.Ordinal);
            var ids = new List<string>();
            foreach (var id in bugIds ?? Array.Empty<string>())
            {
                if (id != null && !ids.Contains(id)) ids.Add(id);
            }
            ids.Sort(StringComparer.Ordinal);

            var shot = new JObject { ["path"] = path, ["t"] = Math.Round(t, 3), ["labels"] = new JArray(labelSet) };
            if (ids.Count > 0) shot["bug_ids"] = new JArray(ids);
            lock (_lock)
            {
                _screenshots.Add(shot);
            }
            foreach (var id in ids)
            {
                Trigger(id, t, scene);
            }
        }

        /// <summary>The labels document; seeded bugs sorted by id, screenshots in the order they were taken.</summary>
        public JObject ToJson(string runId)
        {
            var serializer = JsonSerializer.CreateDefault();
            var bugs = new JArray();
            var shots = new JArray();
            lock (_lock)
            {
                foreach (var shot in _screenshots)
                {
                    shots.Add(shot.DeepClone());
                }
                var ids = new List<string>(_order);
                ids.Sort(StringComparer.Ordinal);
                foreach (var id in ids)
                {
                    var item = JObject.FromObject(_catalog[id], serializer);
                    item["triggers"] = new JArray(_triggers[id]);
                    bugs.Add(item);
                }
            }
            return new JObject
            {
                ["schema"] = SchemaId,
                ["run_id"] = runId,
                ["seeded_bugs"] = bugs,
                ["screenshots"] = shots,
            };
        }

        /// <summary>Write <c>labels.json</c>: indented UTF-8 without BOM, LF line endings.</summary>
        public void WriteTo(string path, string runId)
        {
            var text = ToJson(runId).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
