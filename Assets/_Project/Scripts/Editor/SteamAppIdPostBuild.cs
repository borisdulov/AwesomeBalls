using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace AwesomeBalls.Editor
{
    /// <summary>
    /// Copies steam_appid.txt next to the built executable so dev builds launched outside Steam
    /// can initialize the Steam API (AppID 480 until the game has its own).
    /// </summary>
    public sealed class SteamAppIdPostBuild : IPostprocessBuildWithReport
    {
        const string FileName = "steam_appid.txt";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!File.Exists(FileName)) return;
            string buildDir = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(buildDir) || !Directory.Exists(buildDir)) return;
            File.Copy(FileName, Path.Combine(buildDir, FileName), overwrite: true);
        }
    }
}
