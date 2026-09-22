using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Fails any release build that still has <c>TTTXO_LOCAL_ANALYTICS</c> defined.
    ///
    /// That define makes <c>GameAnalytics</c> write events to a local file and skip Unity Analytics
    /// entirely - correct for a beta, silently wrong for production, where it would mean shipping
    /// with zero telemetry while writing files to players' devices. The failure mode is invisible:
    /// the build succeeds, the game plays fine, and the absence of data only shows up once someone
    /// goes looking for it.
    ///
    /// So the rule is enforced at build time rather than written down: development builds may carry
    /// the define, release builds may not. Same reasoning as
    /// Assets/Scripts/Tests/Game/SeasonConfigAlignmentTests.cs - an invariant that depends on
    /// somebody remembering is an invariant that eventually breaks.
    /// </summary>
    public class LocalAnalyticsBuildGuard : IPreprocessBuildWithReport
    {
        private const string Define = "TTTXO_LOCAL_ANALYTICS";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
            {
                return;
            }

            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(report.summary.platform));

            string defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            if (string.IsNullOrEmpty(defines))
            {
                return;
            }

            foreach (string symbol in defines.Split(';'))
            {
                if (symbol.Trim() != Define)
                {
                    continue;
                }

                throw new BuildFailedException(
                    $"{Define} is set for {namedTarget.TargetName}, but this is a release build. " +
                    "That define sends analytics to a local file and skips Unity Analytics, so this " +
                    "build would ship with no telemetry and would write event files on players' " +
                    "devices. Remove it from Player Settings > Scripting Define Symbols, or tick " +
                    "Development Build if this is a beta.");
            }
        }
    }
}
