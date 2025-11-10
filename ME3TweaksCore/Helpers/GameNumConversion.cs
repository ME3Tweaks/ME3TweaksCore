using LegendaryExplorerCore.Packages;

namespace ME3TweaksCore.Helpers
{
    public class GameNumConversion
    {
        /// <summary>
        /// Converts an ME3Tweaks game ID to the MEGame enum
        /// </summary>
        /// <param name="me3tweaksGameNum"></param>
        /// <returns></returns>
        public static MEGame FromGameNum(int me3tweaksGameNum)
        {
            return me3tweaksGameNum switch
            {
                1 => MEGame.ME1,
                2 => MEGame.ME2,
                3 => MEGame.ME3,
                4 => MEGame.LE1,
                5 => MEGame.LE2,
                6 => MEGame.LE3,
                7 => MEGame.LELauncher,
                8 => MEGame.LMP,
                _ => MEGame.Unknown // Technically we could throw an exception here...
            };
        }
    }
}
