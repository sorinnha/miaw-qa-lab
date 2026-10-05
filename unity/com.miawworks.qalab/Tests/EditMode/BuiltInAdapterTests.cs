// The built-in bots exist only when the project has the AI module and uGUI (the package's version
// defines), so these tests compile under the same conditions.
#if QALAB_AI && QALAB_UGUI
using MiawWorks.QALab;
using NUnit.Framework;
using UnityEngine;

namespace MiawWorks.QALab.Tests
{
    /// <summary>The built-in bots are registered and the UI crawler's rules hold (Unity only: they use UnityEngine).</summary>
    public class BuiltInAdapterTests
    {
        [Test]
        public void BuiltInsAreRegistered()
        {
            Assert.IsTrue(BotAdapterRegistry.IsRegistered(NavMeshExplorerAdapter.AdapterName));
            Assert.IsTrue(BotAdapterRegistry.IsRegistered(UICrawlerAdapter.AdapterName));
            Assert.AreEqual("navmesh_explorer", BotAdapterRegistry.Create("navmesh_explorer").Name);
        }

        [Test]
        public void CrawlerSkipsBlocklistedNamesCaseInsensitively()
        {
            var crawler = new UICrawlerAdapter();
            Assert.IsTrue(crawler.IsBlocked("QuitButton"));
            Assert.IsTrue(crawler.IsBlocked("btn_exit"));
            Assert.IsFalse(crawler.IsBlocked("SettingsButton"));
            Assert.IsTrue(new UICrawlerAdapter(new[] { "Play" }).IsBlocked("PlayButton"));
            Assert.IsFalse(new UICrawlerAdapter(new[] { "Play" }).IsBlocked("QuitButton"), "a custom list replaces the default");
        }

        [Test]
        public void UiPathIsTheHierarchyPath()
        {
            var canvas = new GameObject("MenuCanvas");
            var panel = new GameObject("SettingsPanel");
            var button = new GameObject("ApplyButton");
            panel.transform.SetParent(canvas.transform);
            button.transform.SetParent(panel.transform);
            try
            {
                Assert.AreEqual("MenuCanvas/SettingsPanel/ApplyButton", UICrawlerAdapter.PathOf(button.transform));
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }
    }
}
#endif
