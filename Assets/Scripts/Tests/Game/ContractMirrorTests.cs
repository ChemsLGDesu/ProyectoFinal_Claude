using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TTTXO.Game.Services;
using UnityEngine;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Guards the constants that exist twice on purpose - once in the client and once in the Cloud
    /// Code module - because <c>CloudCode~/TicTacToeModule/</c> only pulls in TTTXO.Core via
    /// <c>&lt;Compile Include&gt;</c>, never the rest of <c>Assets/Scripts/Game/</c>.
    ///
    /// Both sides carry a "bump them together" comment, which is a convention nothing enforced until
    /// now. The handshake version matters most: Docs/01-Directrices-Proyecto.md calls it critical
    /// precisely because there is real-money IAP from day one, and a silent client/server mismatch is
    /// only a warning at runtime (see <c>GameConfigService</c>), so it would reach players unnoticed.
    ///
    /// These read the server source as text rather than compiling it: the module targets .NET 9 and
    /// is not part of any Unity assembly, so text is the only seam available from a test.
    /// </summary>
    [TestFixture]
    public class ContractMirrorTests
    {
        [Test]
        public void ProtocolVersion_MatchesTheCloudCodeModule()
        {
            int serverVersion = ReadIntConstant("GameProtocol.cs", "Version");

            Assert.AreEqual(
                GameProtocol.Version,
                serverVersion,
                "client and Cloud Code GameProtocol.Version drifted - bump both sides together");
        }

        [Test]
        public void RankedModeCode_MatchesTheCloudCodeModule()
        {
            string serverModeCode = ReadStringConstant("RankedMatchSupport.cs", "ModeCode");

            Assert.AreEqual(
                GameAnalytics.RankedModeCode,
                serverModeCode,
                "the Ranked mode code is emitted in Analytics and matched server-side; both must agree");
        }

        private static string ReadModuleSource(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "CloudCode~", "TicTacToeModule", fileName));

            Assert.IsTrue(File.Exists(path), $"Cloud Code source not found at {path}");

            return File.ReadAllText(path);
        }

        private static int ReadIntConstant(string fileName, string constantName)
        {
            var match = Regex.Match(
                ReadModuleSource(fileName),
                @"const\s+int\s+" + Regex.Escape(constantName) + @"\s*=\s*(-?\d+)\s*;");

            Assert.IsTrue(match.Success, $"could not find 'const int {constantName}' in {fileName}");

            return int.Parse(match.Groups[1].Value);
        }

        private static string ReadStringConstant(string fileName, string constantName)
        {
            var match = Regex.Match(
                ReadModuleSource(fileName),
                @"const\s+string\s+" + Regex.Escape(constantName) + @"\s*=\s*""([^""]*)""\s*;");

            Assert.IsTrue(match.Success, $"could not find 'const string {constantName}' in {fileName}");

            return match.Groups[1].Value;
        }
    }
}
