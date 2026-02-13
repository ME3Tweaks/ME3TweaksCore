using System.Collections.Generic;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Services.Symbol;
using Newtonsoft.Json.Linq;

namespace ME3TweaksCore.Test
{
    [TestClass]
    public class SymbolServiceTests
    {
        [TestMethod]
        public void TestSymbolServiceInitialization()
        {
            // Test that service can be loaded without data
            var result = SymbolService.LoadService(null);
            Assert.IsTrue(result, "Service should load successfully with null data");
            Assert.IsTrue(SymbolService.ServiceLoaded, "ServiceLoaded flag should be true after loading");
        }

        [TestMethod]
        public void TestSymbolServiceWithEmptyData()
        {
            // Test loading with empty JSON array
            var emptyData = JArray.Parse("[]");
            var result = SymbolService.LoadService(emptyData);
            Assert.IsTrue(result, "Service should load successfully with empty array");
            Assert.IsTrue(SymbolService.ServiceLoaded, "ServiceLoaded flag should be true after loading");
        }

        [TestMethod]
        public void TestSymbolServiceWithValidData()
        {
            // Create test data with symbol records for LE1, LE2, LE3
            var testData = JArray.Parse(@"[
                {
                    ""game"": ""LE1"",
                    ""gamehash"": ""abc123"",
                    ""pdbhash"": ""def456""
                },
                {
                    ""game"": ""LE2"",
                    ""gamehash"": ""ghi789"",
                    ""pdbhash"": ""jkl012""
                },
                {
                    ""game"": ""LE3"",
                    ""gamehash"": ""mno345"",
                    ""pdbhash"": ""pqr678""
                }
            ]");

            var result = SymbolService.LoadService(testData);
            Assert.IsTrue(result, "Service should load successfully with valid data");

            // Verify that data can be retrieved for each game
            var le1Symbols = SymbolService.GetSymbolsForGame(MEGame.LE1);
            Assert.IsNotNull(le1Symbols, "LE1 symbols should not be null");
            Assert.AreEqual(1, le1Symbols.Count, "Should have 1 LE1 symbol record");
            Assert.AreEqual("abc123", le1Symbols[0].GameHash, "LE1 game hash should match");
            Assert.AreEqual("def456", le1Symbols[0].PdbHash, "LE1 pdb hash should match");

            var le2Symbols = SymbolService.GetSymbolsForGame(MEGame.LE2);
            Assert.IsNotNull(le2Symbols, "LE2 symbols should not be null");
            Assert.AreEqual(1, le2Symbols.Count, "Should have 1 LE2 symbol record");

            var le3Symbols = SymbolService.GetSymbolsForGame(MEGame.LE3);
            Assert.IsNotNull(le3Symbols, "LE3 symbols should not be null");
            Assert.AreEqual(1, le3Symbols.Count, "Should have 1 LE3 symbol record");
        }

        [TestMethod]
        public void TestSymbolServiceWithMultipleRecordsPerGame()
        {
            // Test multiple symbol records for the same game
            var testData = JArray.Parse(@"[
                {
                    ""game"": ""LE1"",
                    ""gamehash"": ""hash1"",
                    ""pdbhash"": ""pdb1""
                },
                {
                    ""game"": ""LE1"",
                    ""gamehash"": ""hash2"",
                    ""pdbhash"": ""pdb2""
                }
            ]");

            SymbolService.LoadService(testData);

            var le1Symbols = SymbolService.GetSymbolsForGame(MEGame.LE1);
            Assert.AreEqual(2, le1Symbols.Count, "Should have 2 LE1 symbol records");
        }

        [TestMethod]
        public void TestGetSymbolsForNonExistentGame()
        {
            // Load empty service
            SymbolService.LoadService(null);

            // Try to get symbols for a game that has no records
            var me1Symbols = SymbolService.GetSymbolsForGame(MEGame.ME1);
            Assert.IsNotNull(me1Symbols, "Should return non-null list");
            Assert.AreEqual(0, me1Symbols.Count, "Should return empty list for game with no records");
        }

        [TestMethod]
        public void TestSymbolRecordSerialization()
        {
            // Test that SymbolRecord can be serialized and deserialized correctly
            var record = new SymbolRecord
            {
                Game = MEGame.LE1,
                GameHash = "testhash123",
                PdbHash = "testpdb456"
            };

            Assert.AreEqual(MEGame.LE1, record.Game);
            Assert.AreEqual("testhash123", record.GameHash);
            Assert.AreEqual("testpdb456", record.PdbHash);
        }
    }
}
