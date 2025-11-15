using System;
using System.Collections.Generic;
using System.IO;
using LegendaryExplorerCore.Gammtek.Extensions;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Helpers;
using ME3TweaksCore.Targets;

namespace ME3TweaksCore.Services.Shared.BasegameFileIdentification
{
    /// <summary>
    /// Information that can be used to identify the source of the mod for a basegame file change.
    /// </summary>
    public class BasegameFileRecord
    {
        protected bool Equals(BasegameFileRecord other)
        {
            return file == other.file && hash == other.hash && size == other.size;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BasegameFileRecord)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(file, hash, size);
        }

        public string file { get; set; }
        public string hash { get; set; }
        public string source { get; set; }
        public string game { get; set; }
        public int size { get; set; }
        public List<string> moddeschashes { get; set; } = new List<string>(4);
        public BasegameFileRecord() { }
        public BasegameFileRecord(string relativePathToRoot, int size, MEGame game, string humanName, string md5)
        {
            file = relativePathToRoot;
            hash = md5 ?? MUtilities.CalculateHash(relativePathToRoot);
            this.game = game.ToGameNum().ToString(); // due to how json serializes stuff we have to convert it here.
            this.size = size;
            source = humanName?.Trim(); // 10/31/2024 - .Trim()
        }

        /// <summary>
        /// Generates a basegame file record from the file path, the given target, and the display name
        /// </summary>
        /// <param name="fullfilepath">The full file path, not a relative one</param>
        /// <param name="target"></param>
        /// <param name="recordedMergeName"></param>
        /// <exception cref="NotImplementedException"></exception>
        public BasegameFileRecord(string fullfilepath, GameTarget target, string recordedMergeName)
        {
            this.file = fullfilepath.Substring(target.TargetPath.Length + 1);
            this.hash = MUtilities.CalculateHash(fullfilepath);
            this.game = target.Game.ToGameNum().ToString();
            this.size = (int)new FileInfo(fullfilepath).Length;
            this.source = recordedMergeName;
        }


        // Data block - used so we can add and remove blocks from the record text. It's essentially a crappy struct.
        public static readonly string BLOCK_OPENING = @"[[";
        public static readonly string BLOCK_CLOSING = @"]]";
        public static readonly string BLOCK_SEPARATOR = @"|"; // This character cannot be in a filename, so it will work better

        /// <summary>
        /// Generates a data block string for the given name and data for use in the source text of a record
        /// </summary>
        /// <param name="blockName"></param>
        /// <param name="blockData"></param>
        /// <returns></returns>
        public static string CreateBlock(string blockName, string blockData)
        {
            return $@"{BLOCK_OPENING}{blockName}={blockData}{BLOCK_CLOSING}";
        }

        /// <summary>
        /// Returns the source string without the block of the given name within it. Use this to subtract blocks out of the string.
        /// </summary>
        /// <param name="blockName"></param>
        /// <returns></returns>
        public string GetWithoutBlock(string blockName)
        {
            string parsingStr = source;
            int openIdx = parsingStr.IndexOf(BLOCK_OPENING);
            int closeIdx = parsingStr.IndexOf(BLOCK_CLOSING);
            while (openIdx >= 0 && closeIdx >= 0 && closeIdx > openIdx)
            {
                var blockText = parsingStr.Substring(openIdx + BLOCK_OPENING.Length, closeIdx - (openIdx + BLOCK_OPENING.Length));
                var blockEqIdx = blockText.IndexOf('=');
                if (blockEqIdx > 0)
                {
                    var pBlockName = blockText.Substring(0, blockEqIdx);
                    if (pBlockName.CaseInsensitiveEquals(blockName))
                    {
                        // The lazy way: Just do a replacement with nothing.
                        return source.Replace(parsingStr.Substring(openIdx, closeIdx - openIdx + BLOCK_CLOSING.Length), @"");
                    }
                }
            }

            // There is edge case where you have ]][[ in the string. Is anyone going to do that? Please don't.
            // We did not find the data block.
            return parsingStr;
        }

        /// <summary>
        /// Gets a string for displaying in a UI - stripping out the block storage
        /// </summary>
        /// <returns></returns>
        public string GetSourceForUI()
        {
            List<string> blockNames = new List<string>();
            List<string> blockValues = new List<string>();

            string parsingStr = source;
            // Find start and end of first data block
            int openIdx = parsingStr.IndexOf(BLOCK_OPENING);
            int closeIdx = parsingStr.IndexOf(BLOCK_CLOSING);
            
            // Read all blocks into blockNames and blockValues
            while (openIdx >= 0 && closeIdx >= 0 && closeIdx > openIdx)
            {
                var blockText = parsingStr.Substring(openIdx + BLOCK_OPENING.Length, closeIdx - (openIdx + BLOCK_OPENING.Length));
                var blockEqIdx = blockText.IndexOf('=');
                if (blockEqIdx > 0)
                {
                    var pBlockName = blockText.Substring(0, blockEqIdx);
                    blockNames.Add(pBlockName);
                    var blockData = blockText[(blockEqIdx+1)..];
                    blockValues.AddRange(blockData.Split(BLOCK_SEPARATOR));
                }

                // Continue
                parsingStr = parsingStr[(closeIdx + BLOCK_CLOSING.Length)..];
                openIdx = parsingStr.IndexOf(BLOCK_OPENING);
                closeIdx = parsingStr.IndexOf(BLOCK_CLOSING);
            }
            
            // Reset parsing string back to original value as we have now extracted data
            parsingStr = source;

            // Remove all data blocks from the string. Non-block data will be all that remains
            foreach (var block in blockNames)
            {
                parsingStr = GetWithoutBlock(block);
            }

            if (blockValues.Any())
            {
                parsingStr += "\n"; // do not localize
                parsingStr += string.Join("\n", blockValues); // do not localize
            }
            
            return parsingStr.Trim();
        }

        /// <summary>
        /// Retrieves a block by its name. Can return null.
        /// </summary>
        /// <param name="blockName">The name of the block to retrieve. Cannot be null or empty.</param>
        internal string GetBlock(string blockName)
        {
            string parsingStr = source;
            int openIdx = parsingStr.IndexOf(BLOCK_OPENING);
            int closeIdx = parsingStr.IndexOf(BLOCK_CLOSING);
            while (openIdx >= 0 && closeIdx >= 0 && closeIdx > openIdx)
            {
                var blockText = parsingStr.Substring(openIdx + BLOCK_OPENING.Length, closeIdx - (openIdx + BLOCK_OPENING.Length));
                var blockEqIdx = blockText.IndexOf('=');
                if (blockEqIdx > 0)
                {
                    var pBlockName = blockText.Substring(0, blockEqIdx);
                    if (pBlockName.CaseInsensitiveEquals(blockName))
                    {
                        // Return the found block's content
                        return blockText.Substring(blockText.IndexOf('=')+1);
                    }
                }
            }
            
            // Block not found
            return null;
        }
    }
}
