using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.Coalesced.Config;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Config;

namespace ME3TweaksCore.Test
{
    [TestClass]
    public class M3CConfigMergeTests
    {
        private const string DLCName = @"DLC_MOD_ConfigMergeTest";

        /// <summary>
        /// Builds a game 2 DLC folder with a BioGame.ini and a single m3cd, runs the DLC config merge, and returns the resulting BioGame.ini values for SFXGame.Test Key.
        /// </summary>
        private static List<CoalesceValue> RunGame2DLCMerge(MEGame game, string baseIni, string m3cd)
        {
            var root = Path.Combine(Path.GetTempPath(), @"M3CConfigMergeTests_" + Guid.NewGuid().ToString(@"N"));
            try
            {
                var cookedDir = Path.Combine(root, DLCName, game.CookedDirName());
                Directory.CreateDirectory(cookedDir);
                var bioGameIni = Path.Combine(cookedDir, @"BioGame.ini");
                File.WriteAllText(bioGameIni, baseIni);
                File.WriteAllText(Path.Combine(cookedDir, ConfigMerge.CONFIG_MERGE_PREFIX + @"Test" + ConfigMerge.CONFIG_MERGE_EXTENSION), m3cd);

                M3CConfigMerge.PerformDLCMerge(game, root, DLCName);

                var result = ConfigFileProxy.LoadIni(bioGameIni);
                Assert.IsTrue(result.Sections.TryGetValue(@"SFXGame.Test", out var section), "Merged ini is missing section SFXGame.Test");
                Assert.IsTrue(section.TryGetValue(@"Key", out var prop), "Merged ini is missing property Key");
                return prop.ToList();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void TestOverwriteOnlyDeltaIsCommitted()
        {
            // A delta that only overwrites (>) an existing key must be written back to disk.
            var values = RunGame2DLCMerge(MEGame.LE2,
                "[SFXGame.Test]\nKey=Old\n",
                "[BioGame.ini SFXGame.Test]\n>Key=New\n");

            Assert.AreEqual(1, values.Count, $"Expected a single value after overwrite, got: {string.Join(", ", values.Select(x => x.Value))}");
            Assert.AreEqual(@"New", values[0].Value, "Overwrite-only config delta was not committed to the DLC config");
        }

        [TestMethod]
        public void TestAddUniqueDeltaIsCommitted()
        {
            // Control: an add-unique delta flags a change and is committed.
            var values = RunGame2DLCMerge(MEGame.LE2,
                "[SFXGame.Test]\nKey=Old\n",
                "[BioGame.ini SFXGame.Test]\n+Key=Added\n");

            CollectionAssert.AreEqual(new[] { @"Old", @"Added" }, values.Select(x => x.Value).ToArray());
        }
    }
}
