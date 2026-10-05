#if QALAB_UGUI
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Built-in bot <c>ui_crawler</c> (spec 01): every step it clicks one random visible, interactable uGUI
    /// control (buttons, toggles, dropdowns, ...). Menus are where "nobody ever pressed Apply with nothing
    /// changed" bugs live, and a seeded random clicker finds them without a script per screen.
    /// <list type="bullet">
    /// <item>Candidates: active, interactable <see cref="Selectable"/>s, sorted by hierarchy path so the
    /// same seed picks the same control in the same menu.</item>
    /// <item>Click: <c>ExecuteEvents.Execute(go, pointer event, pointerClickHandler)</c>, the same path a
    /// mouse click takes. Exceptions in the game's click handler are logged by Unity (and captured),
    /// never thrown here.</item>
    /// <item>The click is logged (<c>click</c> with <c>ui_path</c>) before it runs, so it comes before any
    /// error it causes.</item>
    /// <item>Names containing a blocklisted word (default Quit, Exit) are never clicked.</item>
    /// </list>
    /// </summary>
    public sealed class UICrawlerAdapter : IBotAdapter
    {
        public const string AdapterName = "ui_crawler";

        private readonly string[] _blocklist;
        private readonly List<Selectable> _candidates = new List<Selectable>();
        private readonly List<string> _paths = new List<string>();

        /// <param name="blocklist">Case-insensitive words; a control whose name contains one is skipped.
        /// Null = Quit and Exit.</param>
        public UICrawlerAdapter(IEnumerable<string> blocklist = null)
        {
            _blocklist = new List<string>(blocklist ?? new[] { "Quit", "Exit" }).ToArray();
        }

        public string Name => AdapterName;

        public void Begin(BotContext ctx)
        {
        }

        public BotStepResult Step(BotContext ctx)
        {
            Collect();
            if (_candidates.Count == 0)
            {
                return BotStepResult.Continue;   // nothing clickable on screen right now
            }
            var index = ctx.Random.Next(_candidates.Count);
            var target = _candidates[index].gameObject;
            ctx.LogAction("click", null, _paths[index]);
            ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            return BotStepResult.Continue;
        }

        public void End(BotContext ctx)
        {
        }

        /// <summary>True if <paramref name="name"/> contains a blocklisted word.</summary>
        public bool IsBlocked(string name)
        {
            foreach (var word in _blocklist)
            {
                if (!string.IsNullOrEmpty(word) && name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void Collect()
        {
            _candidates.Clear();
            _paths.Clear();
            var found = new List<(string Path, Selectable Control)>();
            foreach (var selectable in Selectable.allSelectablesArray)
            {
                if (selectable == null || !selectable.IsInteractable() || !selectable.gameObject.activeInHierarchy) continue;
                if (IsBlocked(selectable.gameObject.name)) continue;
                found.Add((PathOf(selectable.transform), selectable));
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            foreach (var (path, control) in found)
            {
                _paths.Add(path);
                _candidates.Add(control);
            }
        }

        /// <summary>Hierarchy path, e.g. <c>Canvas/SettingsPanel/Apply</c>.</summary>
        public static string PathOf(Transform transform)
        {
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            var path = new StringBuilder();
            foreach (var part in parts)
            {
                if (path.Length > 0) path.Append('/');
                path.Append(part);
            }
            return path.ToString();
        }
    }
}
#endif
