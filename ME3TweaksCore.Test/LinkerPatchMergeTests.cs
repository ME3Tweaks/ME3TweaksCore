using System.Reflection;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Coalesced;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using ME3TweaksCore.Exceptions;
using ME3TweaksCore.GameFilesystem;
using ME3TweaksCore.Localization;
using ME3TweaksCore.ME3Tweaks.M3Merge.LinkerPatches;
using ME3TweaksCore.ME3Tweaks.ModManager;
using Newtonsoft.Json;

namespace ME3TweaksCore.Test
{
    [TestClass]
    public class LinkerPatchMergeTests
    {
        private const MEGame Game = MEGame.LMP;
        private string root = null!;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            LegendaryExplorerCoreLib.InitLib(TaskScheduler.FromCurrentSynchronizationContext(), x => { }, objectDBsToLoad: [MEGame.LMP]);

            // ME3TweaksCore is not initialized in tests, so LC.SetLanguage does nothing. Load INT strings directly so error messages can be checked.
            typeof(LC).GetMethod(@"InternalSetLanguage", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [@"INT"]);
        }

        [TestInitialize]
        public void TestInit()
        {
            // Fake LMP game root. Only the folders the merge looks at are created.
            root = Path.Combine(Path.GetTempPath(), @"LinkerPatchMergeTests_" + Guid.NewGuid().ToString(@"N"));
            Directory.CreateDirectory(Path.Combine(root, @"BioGame", Game.CookedDirName()));
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        #region Helpers
        private string OutputRoot => LinkerPatchMerge.GetLinkerPatchesRootPath(Game, root);
        private string OutputCooked => LinkerPatchMerge.GetLinkerPatchesCookedPath(Game, root);

        /// <summary>
        /// Creates an enabled DLC folder with a mount file and a metacmm, and returns its LinkerPatches folder
        /// </summary>
        private string CreateDLC(string dlcName, int mountPriority, double featureLevel = ModDescConsts.MODDESC_VERSION_10_0)
        {
            var dlcDir = Path.Combine(root, @"BioGame", @"DLC", dlcName);
            var cookedDir = Path.Combine(dlcDir, Game.CookedDirName());
            Directory.CreateDirectory(cookedDir);
            new MountFile(Game) { MountPriority = mountPriority, MountFlags = new MountFlag(EME3MountFileFlag.LoadsInSingleplayer) }.WriteMountFile(Path.Combine(cookedDir, @"Mount.dlc"));
            new MetaCMM() { ModName = $@"{dlcName} Mod", Version = @"1.0", ModDescFeatureLevel = featureLevel }.WriteMetaCMM(Path.Combine(dlcDir, @"_metacmm.txt"), @"Test");
            var lpDir = Path.Combine(dlcDir, LinkerPatchMerge.LINKER_PATCH_DLC_FOLDERNAME);
            Directory.CreateDirectory(lpDir);
            return lpDir;
        }

        /// <summary>
        /// Creates an uncompressed patch package with an export at each of the given object paths. Outers are created as forced Package exports, as in cooked files.
        /// </summary>
        private static void CreatePatchPackage(string path, IEnumerable<string> objectPaths, bool cooked = true, string? rootImport = null, string? outerClass = null)
        {
            using var pcc = MEPackageHandler.CreateMemoryEmptyPackage(path, Game);
            foreach (var objectPath in objectPaths)
            {
                IEntry? parent = null;
                var segments = objectPath.Split('.');
                for (int i = 0; i < segments.Length; i++)
                {
                    var existing = pcc.Exports.FirstOrDefault(x => x.Parent == parent && x.ObjectName.Instanced == segments[i]);
                    if (existing == null)
                    {
                        existing = pcc.CreatePackageExport(segments[i], parent, forcedExport: i == 0 && segments.Length > 1);
                        if (outerClass != null && i == 1 && segments.Length > 2)
                        {
                            existing.Class = pcc.GetEntryOrAddImport($@"Engine.{outerClass}", @"Class", @"Engine");
                        }
                    }
                    parent = existing;
                }
            }

            if (rootImport != null)
            {
                pcc.AddImport(new ImportEntry(pcc, 0, rootImport) { ClassName = @"Package", PackageFile = @"Core" });
            }

            if (!cooked)
            {
                pcc.setFlags(pcc.Flags & ~UnrealFlags.EPackageFlags.Cooked);
            }
            pcc.Save(path, compress: false);
        }

        private static void WriteManifest(string path, params (string original, string package, string? description)[] patches)
        {
            WriteManifest(path, patches.Select(x => new LinkerPatchManifestEntry() { OriginalObject = x.original, PatchPackage = x.package, Description = x.description }).ToArray());
        }

        private static void WriteManifest(string path, params LinkerPatchManifestEntry[] patches)
        {
            var manifest = new LinkerPatchManifest()
            {
                Game = Game,
                Description = @"Test manifest",
                Patches = patches.ToList()
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
        }

        private void RunMerge() => LinkerPatchMerge.RunLinkerPatchMerge(Game, root, null, validateOriginalsAgainstGame: false);
        private void RunValidatedMerge() => LinkerPatchMerge.RunLinkerPatchMerge(Game, root, null, validateOriginalsAgainstGame: true);

        private List<CoalesceValue> ReadIndexRedirects()
        {
            var assets = CoalescedConverter.DecompileGame3ToAssets(Path.Combine(OutputCooked, LinkerPatchMerge.LINKER_PATCH_INDEX_FILENAME));
            Assert.AreEqual(1, assets.Count, "Index should contain exactly one asset");
            var asset = assets.Values.First();
            Assert.AreEqual(@"..\..\biogame\config\bioengine.ini", asset.Source, "Index asset source is wrong");
            Assert.IsTrue(asset.Sections.TryGetValue(@"linkerpatchobjects", out var section), "Index is missing the linkerpatchobjects section");
            Assert.IsTrue(section.TryGetValue(@"redirects", out var prop), "Index is missing the redirects property");
            return prop.ToList();
        }
        #endregion

        [TestMethod]
        public void TestOutputLayoutAndIndexRoundTrip()
        {
            var lp = CreateDLC(@"DLC_MOD_Alpha", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_Weapons.pcc"), [@"SFXGame.SFXWeapon_Avenger", @"GUI_SF_MainMenu.MainMenu"]);
            WriteManifest(Path.Combine(lp, @"Weapons.m3lp"),
                (@"SFXGame.SFXWeapon_Avenger", @"Patch_Weapons.pcc", @"Fixes Avenger"),
                (@"GUI_SF_MainMenu.MainMenu", @"Patch_Weapons.pcc", null));

            Assert.IsTrue(LinkerPatchMerge.NeedsMerged(Game, root));
            RunMerge();

            // Layout
            Assert.IsTrue(File.Exists(Path.Combine(OutputCooked, @"Patch_DLC_MOD_Alpha_Weapons.pcc")), "Patch package was not installed with its renamed filename");
            Assert.IsFalse(File.Exists(Path.Combine(OutputCooked, @"Patch_Weapons.pcc")), "Patch package was installed with its source filename");
            Assert.IsTrue(File.Exists(LinkerPatchMerge.GetBuildRecordPath(Game, root)), "Build record was not written");

            // Index round trip
            var redirects = ReadIndexRedirects();
            Assert.IsTrue(redirects.All(x => x.ValueType == 3), "All redirects must be type 3 (add unique)");
            CollectionAssert.AreEquivalent(new[]
            {
                "(OriginalObject=\"SFXGame.SFXWeapon_Avenger\",PatchObject=\"Patch_DLC_MOD_Alpha_Weapons\")",
                "(OriginalObject=\"GUI_SF_MainMenu.MainMenu\",PatchObject=\"Patch_DLC_MOD_Alpha_Weapons\")",
            }, redirects.Select(x => x.Value).ToArray());

            // Build record
            var record = LinkerPatchMerge.LoadBuildRecord(Game, root);
            Assert.IsNotNull(record);
            Assert.AreEqual(1, record.SourceManifests.Count);
            Assert.AreEqual(2, record.Redirects.Count);
            var avenger = record.Redirects.Single(x => x.OriginalObject == @"SFXGame.SFXWeapon_Avenger");
            Assert.AreEqual(@"DLC_MOD_Alpha", avenger.SourceDLC);
            Assert.AreEqual(@"DLC_MOD_Alpha Mod", avenger.SourceModName);
            Assert.AreEqual(@"Fixes Avenger", avenger.PatchDescription);
            Assert.AreEqual(@"Test manifest", avenger.ManifestDescription);
            Assert.IsFalse(LinkerPatchMerge.IsBuildStale(Game, root, record), "A fresh build should not be stale");
        }

        [TestMethod]
        public void TestHigherMountPriorityWins()
        {
            var lpLow = CreateDLC(@"DLC_MOD_Low", 100);
            CreatePatchPackage(Path.Combine(lpLow, @"Patch_Low.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lpLow, @"Low.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_Low.pcc", @"Low patch"));

            // Created first but has higher priority, to make sure mount order is used rather than folder order
            var lpHigh = CreateDLC(@"DLC_MOD_AHigh", 500);
            CreatePatchPackage(Path.Combine(lpHigh, @"Patch_High.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lpHigh, @"High.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_High.pcc", @"High patch"));

            RunMerge();

            var redirects = ReadIndexRedirects();
            Assert.AreEqual(1, redirects.Count);
            Assert.AreEqual("(OriginalObject=\"SFXGame.SFXWeapon_Avenger\",PatchObject=\"Patch_DLC_MOD_AHigh_High\")", redirects[0].Value);
            Assert.IsTrue(File.Exists(Path.Combine(OutputCooked, @"Patch_DLC_MOD_AHigh_High.pcc")));
            Assert.IsFalse(File.Exists(Path.Combine(OutputCooked, @"Patch_DLC_MOD_Low_Low.pcc")), "Overridden patch package with no other patches should not be installed");

            var record = LinkerPatchMerge.LoadBuildRecord(Game, root)!;
            var redirect = record.Redirects.Single();
            Assert.AreEqual(1, redirect.Overridden.Count);
            Assert.AreEqual(@"DLC_MOD_Low", redirect.Overridden[0].DLC);
            Assert.AreEqual(@"Low patch", redirect.Overridden[0].PatchDescription);
        }

        [TestMethod]
        public void TestDuplicateWithinDLCFailsAndRemovesOutput()
        {
            var lp = CreateDLC(@"DLC_MOD_Dupe", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            CreatePatchPackage(Path.Combine(lp, @"Patch_B.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));

            // Successful build first, so there is output to remove
            RunMerge();
            Assert.IsTrue(Directory.Exists(OutputRoot));

            WriteManifest(Path.Combine(lp, @"B.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_B.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"more than one linker patch");
            Assert.IsFalse(Directory.Exists(OutputRoot), "Output folder must be deleted when the merge fails");
        }

        [TestMethod]
        public void TestWrongCasePatchPrefixFails()
        {
            var lp = CreateDLC(@"DLC_MOD_Case", 100);
            CreatePatchPackage(Path.Combine(lp, @"patch_Foo.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"Case.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"patch_Foo.pcc", null));

            Assert.IsFalse(LinkerPatchMerge.IsValidPatchPackageName(@"patch_Foo.pcc"));
            Assert.IsFalse(LinkerPatchMerge.IsValidPatchPackageName(@"PATCH_Foo.pcc"));
            Assert.IsFalse(LinkerPatchMerge.IsValidPatchPackageName(@"Foo.pcc"));
            Assert.IsFalse(LinkerPatchMerge.IsValidPatchPackageName(@"Patch_Foo.upk"));
            Assert.IsFalse(LinkerPatchMerge.IsValidPatchPackageName(@"..\Patch_Foo.pcc"));
            Assert.IsTrue(LinkerPatchMerge.IsValidPatchPackageName(@"Patch_Foo.pcc"));

            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"must have a filename that starts with Patch_");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestRemovingAllContributorsDeletesOutput()
        {
            var lp = CreateDLC(@"DLC_MOD_Gone", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            var manifest = Path.Combine(lp, @"A.m3lp");
            WriteManifest(manifest, (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            RunMerge();
            Assert.IsTrue(Directory.Exists(OutputCooked));

            File.Delete(manifest);
            Assert.IsTrue(LinkerPatchMerge.NeedsMerged(Game, root), "Leftover output must trigger a merge so it is cleaned up");
            Assert.IsTrue(LinkerPatchMerge.IsBuildStale(Game, root, LinkerPatchMerge.LoadBuildRecord(Game, root)));
            RunMerge();
            Assert.IsFalse(Directory.Exists(OutputRoot), "Output folder must be deleted when nothing contributes");
            Assert.IsFalse(LinkerPatchMerge.NeedsMerged(Game, root));
        }

        [TestMethod]
        public void TestModdescFeatureLevelGate()
        {
            var lp = CreateDLC(@"DLC_MOD_Old", 100, ModDescConsts.MODDESC_VERSION_9_2);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));

            Assert.AreEqual(0, LinkerPatchMerge.GetContributingManifests(Game, root).Count);
            Assert.IsFalse(LinkerPatchMerge.NeedsMerged(Game, root));
            Assert.IsFalse(LinkerPatchMerge.IsBuildStale(Game, root, null));
            Assert.IsFalse(LinkerPatchMerge.NeedsMerged(MEGame.LE3, root), "Only LMP supports linker patches");
        }

        [TestMethod]
        public void TestEditedManifestIsStale()
        {
            var lp = CreateDLC(@"DLC_MOD_Stale", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger", @"SFXGame.SFXWeapon_Mattock"]);
            var manifest = Path.Combine(lp, @"A.m3lp");
            WriteManifest(manifest, (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            RunMerge();
            Assert.IsFalse(LinkerPatchMerge.IsBuildStale(Game, root, LinkerPatchMerge.LoadBuildRecord(Game, root)));

            WriteManifest(manifest, (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null), (@"SFXGame.SFXWeapon_Mattock", @"Patch_A.pcc", null));
            Assert.IsTrue(LinkerPatchMerge.IsBuildStale(Game, root, LinkerPatchMerge.LoadBuildRecord(Game, root)), "Edited manifest should make the build stale");

            RunMerge();
            Assert.IsFalse(LinkerPatchMerge.IsBuildStale(Game, root, LinkerPatchMerge.LoadBuildRecord(Game, root)));
            Assert.AreEqual(2, ReadIndexRedirects().Count);
        }

        [TestMethod]
        public void TestPatchObjectMustMirrorOriginalPath()
        {
            // Top level object with the right name, but not at the original's path
            var lp = CreateDLC(@"DLC_MOD_Flat", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"does not contain SFXGame.SFXWeapon_Avenger");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestSameLeafInDifferentTreesSharesPackage()
        {
            var lp = CreateDLC(@"DLC_MOD_Leaves", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"GUI_SF_A.Banner", @"GUI_SF_B.Banner"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"),
                (@"GUI_SF_A.Banner", @"Patch_A.pcc", null),
                (@"GUI_SF_B.Banner", @"Patch_A.pcc", null));
            RunMerge();
            Assert.AreEqual(2, ReadIndexRedirects().Count);
        }

        [TestMethod]
        public void TestLevelObjectIsRejected()
        {
            Assert.IsTrue(LinkerPatchMerge.IsLevelObjectPath(@"BioP_MPCommon.TheWorld.PersistentLevel.SeqAct_Log_3"));
            Assert.IsTrue(LinkerPatchMerge.IsLevelObjectPath(@"BioP_MPCommon.theworld"));
            Assert.IsFalse(LinkerPatchMerge.IsLevelObjectPath(@"GUI_SF_Splash.Splash_I2"));

            var lp = CreateDLC(@"DLC_MOD_Level", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"BioP_MPCommon.TheWorld.PersistentLevel.SeqAct_Log_3"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"BioP_MPCommon.TheWorld.PersistentLevel.SeqAct_Log_3", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"because it is part of a level");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestOriginalInsideWorldClassIsRejected()
        {
            // Not named TheWorld, but the game's original is inside a World, so it is still a level object
            CreatePatchPackage(Path.Combine(root, @"BioGame", Game.CookedDirName(), @"BioP_Odd.pcc"), [@"OddPkg.OddWorld.Thing"], outerClass: @"World");

            var lp = CreateDLC(@"DLC_MOD_OddLevel", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"OddPkg.OddWorld.Thing"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"OddPkg.OddWorld.Thing", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunValidatedMerge);
            StringAssert.Contains(ex.Message, @"because it is part of a level");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestPatchPackageMustBeCooked()
        {
            var lp = CreateDLC(@"DLC_MOD_Uncooked", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"], cooked: false);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"is not marked as cooked");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestMissingPatchPackageFails()
        {
            var lp = CreateDLC(@"DLC_MOD_Missing", 100);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"was not found in the LinkerPatches folder");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestOutputCollidingWithGamePackageFails()
        {
            var lp = CreateDLC(@"DLC_MOD_Collide", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));

            // A real game package with the installed name always wins package lookup
            File.WriteAllBytes(Path.Combine(root, @"BioGame", Game.CookedDirName(), @"Patch_DLC_MOD_Collide_A.pcc"), [0]);
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, @"has the same name as a package");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        [DataRow(@"Patch_A", DisplayName = "Own package")]
        [DataRow(@"Patch_Other", DisplayName = "Another patch package")]
        public void TestImportOfPatchPackageFails(string importedPackage)
        {
            var lp = CreateDLC(@"DLC_MOD_SelfRef", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"], rootImport: importedPackage);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunMerge);
            StringAssert.Contains(ex.Message, $@"imports {importedPackage}, which is inside a linker patch package");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestOriginalNotFoundFails()
        {
            var lp = CreateDLC(@"DLC_MOD_NoOriginal", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), (@"SFXGame.SFXWeapon_Avenger", @"Patch_A.pcc", null));
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunValidatedMerge);
            StringAssert.Contains(ex.Message, @"could not be found in the game files");
            Assert.IsFalse(Directory.Exists(OutputRoot));
        }

        [TestMethod]
        public void TestOriginalPackageHintNotInGameSkipsValidation()
        {
            // Targets an object from another mod that isn't installed. The hint is the only evidence of that, so the patch is installed unvalidated.
            var lp = CreateDLC(@"DLC_MOD_OtherModPatch", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGameContent_OtherMod.SFXWeapon_Custom"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), new LinkerPatchManifestEntry() { OriginalObject = @"SFXGameContent_OtherMod.SFXWeapon_Custom", PatchPackage = @"Patch_A.pcc", OriginalPackage = @"Startup_DLC_MOD_OtherMod.pcc" });
            RunValidatedMerge();
            Assert.AreEqual(1, ReadIndexRedirects().Count);
        }

        [TestMethod]
        public void TestOriginalPackageHintInGameIsNotAuthoritative()
        {
            // The hinted package is in the game, so an original that can't be found anywhere is still an error
            var gamePackage = Path.Combine(root, @"BioGame", Game.CookedDirName(), @"SFXGame.pcc");
            CreatePatchPackage(gamePackage, [@"SFXWeapon_Mattock"]);

            var lp = CreateDLC(@"DLC_MOD_BadHint", 100);
            CreatePatchPackage(Path.Combine(lp, @"Patch_A.pcc"), [@"SFXGame.SFXWeapon_Avenger"]);
            WriteManifest(Path.Combine(lp, @"A.m3lp"), new LinkerPatchManifestEntry() { OriginalObject = @"SFXGame.SFXWeapon_Avenger", PatchPackage = @"Patch_A.pcc", OriginalPackage = @"SFXGame.pcc" });
            var ex = Assert.ThrowsExactly<LinkerPatchMergeException>(RunValidatedMerge);
            StringAssert.Contains(ex.Message, @"could not be found in the game files");

            // Found through the hint with a matching class
            WriteManifest(Path.Combine(lp, @"A.m3lp"), new LinkerPatchManifestEntry() { OriginalObject = @"SFXGame.SFXWeapon_Mattock", PatchPackage = @"Patch_B.pcc", OriginalPackage = @"SFXGame.pcc" });
            CreatePatchPackage(Path.Combine(lp, @"Patch_B.pcc"), [@"SFXGame.SFXWeapon_Mattock"]);
            RunValidatedMerge();
            Assert.AreEqual(1, ReadIndexRedirects().Count);
        }
    }
}
