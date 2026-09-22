using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// One-command Android beta build for handing the game to testers.
    ///
    /// The point is that the settings a tester build depends on stop being things somebody has to
    /// remember in the Player Settings inspector. Every one of them below was wrong or unset the
    /// first time an Android build was attempted: the application identifier was still the
    /// <c>com.DefaultCompany.2D-URP</c> from the project template, the company name was
    /// <c>DefaultCompany</c>, and the version was <c>1.0</c> while the project had shipped five
    /// milestones. None of those fail the build - they just produce an APK that installs under the
    /// wrong identity and reports the wrong version in every bug report.
    ///
    /// Beta builds MUST be development builds. <see cref="LocalAnalyticsBuildGuard"/> aborts a
    /// release build that still defines <c>TTTXO_LOCAL_ANALYTICS</c>, and the beta wants that define
    /// (events go to a local file instead of billable Unity Analytics custom events). So
    /// <see cref="BuildOptions.Development"/> is not a convenience here, it is what makes the build
    /// legal - see <see cref="BuildBeta"/>.
    /// </summary>
    public static class BetaBuild
    {
        /// <summary>
        /// Reverse-DNS application identifier. Permanent: on Google Play this is the app's identity
        /// forever, and two APKs signed with different keys under the same identifier cannot upgrade
        /// each other. Chosen 2026-08-11 with the project owner.
        /// </summary>
        private const string ApplicationIdentifier = "com.hellscythe25.tttxo";

        private const string CompanyName = "Hellscythe25";

        private const string OutputDirectory = "Builds/Android";

        /// <summary>
        /// Environment variables the keystore is read from, so the password never lands in
        /// <c>ProjectSettings.asset</c> (which is versioned) or in a script in the repo. When they
        /// are absent the build falls back to Unity's debug keystore, which is fine for a link-shared
        /// APK but cannot be upgraded to a release-signed one later without uninstalling first.
        /// </summary>
        private const string KeystorePathVariable = "TTTXO_KEYSTORE_PATH";

        private const string KeystorePasswordVariable = "TTTXO_KEYSTORE_PASS";

        private const string KeyAliasNameVariable = "TTTXO_KEY_ALIAS";

        private const string KeyAliasPasswordVariable = "TTTXO_KEY_PASS";

        [MenuItem("TTTXO/Build/Android Beta APK")]
        public static void BuildAndroidBetaFromMenu()
        {
            string apkPath = BuildBeta();
            if (apkPath == null)
            {
                return;
            }

            EditorUtility.RevealInFinder(apkPath);
        }

        /// <summary>
        /// Programmatic entry point: builds and returns the APK path (<c>null</c> on failure) without
        /// opening a file browser or quitting the Editor, so tooling can drive it.
        /// </summary>
        public static string BuildAndroidBeta()
        {
            return BuildBeta();
        }

        /// <summary>
        /// Batch-mode entry point: <c>Unity.exe -quit -batchmode -projectPath . -executeMethod
        /// TTTXO.Game.Editor.BetaBuild.BuildAndroidBetaFromCommandLine</c>. Exits non-zero on
        /// failure so a shell wrapper can tell a broken build from a good one - batch mode returns 0
        /// even after a failed build otherwise.
        /// </summary>
        public static void BuildAndroidBetaFromCommandLine()
        {
            if (BuildBeta() == null)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Applies the shipping identity to the Android target without building. Safe to run at any
        /// time and idempotent; the build calls it too, so it never runs on stale settings.
        /// </summary>
        [MenuItem("TTTXO/Build/Apply Android Identity")]
        public static void ApplyAndroidIdentity()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);

            // Both ABIs, deliberately. The project shipped ARM64-only against a minSdk of 25, and
            // those two contradict each other: API 25 is Android 7.1 (2016), an era with plenty of
            // 32-bit-only phones, and on one of those an ARM64-only APK does not fail to launch - it
            // fails to *install*, with INSTALL_FAILED_NO_MATCHING_ABIS, which reaches the user as
            // nothing more than "App not installed".
            //
            // The same gap explains why the first beta ran on MuMu and not on BlueStacks: MuMu 12
            // translates ARM, several BlueStacks builds do not. Carrying armeabi-v7a roughly doubles
            // the APK because IL2CPP compiles twice; that is the price of matching what minSdk
            // already promises. Raising minSdk instead would be the other consistent answer.
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // Pinned, not Automatic. Automatic resolves to the highest SDK platform installed on the
            // machine doing the build - it silently produced 36 here, and would produce something
            // else on a machine with a different SDK installed, changing the app's behaviour with no
            // code change and nothing in the diff to show for it. A target level is a behavioural
            // contract with the OS, so it belongs in source.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;

            SaveProjectSettings();

            Debug.Log(
                $"[BetaBuild] Android identity applied: {ApplicationIdentifier} " +
                $"(company \"{CompanyName}\", version {PlayerSettings.bundleVersion}, " +
                $"versionCode {PlayerSettings.Android.bundleVersionCode}).");
        }

        /// <summary>
        /// Builds the tester APK and returns its path, or <c>null</c> if the build failed.
        /// </summary>
        private static string BuildBeta()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BetaBuild] Leave play mode before building.");
                return null;
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError(
                    "[BetaBuild] No enabled scenes in Build Settings. Expected Assets/Scenes/Main.unity - " +
                    "the whole game is one persistent scene, so an empty list here builds a black screen.");
                return null;
            }

            ApplyAndroidIdentity();

            // Every build gets its own versionCode. Android refuses to install an APK whose
            // versionCode is lower than the installed one, and testers who sideload two builds with
            // the same code get whichever they installed first with no warning.
            PlayerSettings.Android.bundleVersionCode++;
            ApplyKeystore();
            SaveProjectSettings();

            // APK, not AAB: testers install this by hand off a link, and an app bundle is not
            // installable without Play or bundletool.
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(OutputDirectory);
            string apkPath = Path.Combine(
                OutputDirectory,
                $"TTTXO-v{PlayerSettings.bundleVersion}-b{PlayerSettings.Android.bundleVersionCode}-beta.apk");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,

                // Mandatory, not cosmetic: LocalAnalyticsBuildGuard fails any non-development build
                // that still defines TTTXO_LOCAL_ANALYTICS, and the beta wants that define.
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[BetaBuild] Build {summary.result} after {summary.totalTime}. " +
                    $"{summary.totalErrors} error(s). See the Console and Editor.log for the cause.");
                return null;
            }

            double megabytes = summary.totalSize / (1024d * 1024d);
            Debug.Log(
                $"[BetaBuild] APK ready: {apkPath} ({megabytes:F1} MB, {summary.totalTime} to build). " +
                $"Version {PlayerSettings.bundleVersion} (versionCode {PlayerSettings.Android.bundleVersionCode}).");

            return apkPath;
        }

        /// <summary>
        /// Points the build at a release keystore when the environment supplies one, so consecutive
        /// tester builds stay upgradeable in place. Without it Unity signs with its debug keystore:
        /// the APK installs fine, but the day the signature changes every tester has to uninstall,
        /// which wipes their local wallet and match history.
        /// </summary>
        private static void ApplyKeystore()
        {
            string keystorePath = Environment.GetEnvironmentVariable(KeystorePathVariable);
            string keystorePassword = Environment.GetEnvironmentVariable(KeystorePasswordVariable);
            string aliasName = Environment.GetEnvironmentVariable(KeyAliasNameVariable);
            string aliasPassword = Environment.GetEnvironmentVariable(KeyAliasPasswordVariable);

            bool configured =
                !string.IsNullOrEmpty(keystorePath) &&
                !string.IsNullOrEmpty(keystorePassword) &&
                !string.IsNullOrEmpty(aliasName) &&
                !string.IsNullOrEmpty(aliasPassword);

            if (!configured)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning(
                    $"[BetaBuild] No release keystore ({KeystorePathVariable}, {KeystorePasswordVariable}, " +
                    $"{KeyAliasNameVariable}, {KeyAliasPasswordVariable} not all set). Signing with Unity's " +
                    "debug keystore - installable, but a later switch to a real key forces every tester to " +
                    "uninstall and lose their local data. See Docs/10-Beta-Testers.md.");
                return;
            }

            if (!File.Exists(keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning(
                    $"[BetaBuild] {KeystorePathVariable} points at \"{keystorePath}\", which does not exist. " +
                    "Falling back to the debug keystore.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = aliasName;
            PlayerSettings.Android.keyaliasPass = aliasPassword;

            Debug.Log($"[BetaBuild] Signing with the release keystore at \"{keystorePath}\".");
        }

        /// <summary>
        /// Unity buffers Project Settings edits and only writes <c>ProjectSettings.asset</c> when the
        /// project is saved. Without this the settings applied above are correct in the running
        /// Editor and absent from disk, so the next build - or anyone else's clone - silently uses
        /// the old ones.
        /// </summary>
        private static void SaveProjectSettings()
        {
            AssetDatabase.SaveAssets();
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }
    }
}
