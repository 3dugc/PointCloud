using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class PointCloudDiagnosticJournalTests
    {
        [Test]
        public void JournalFlushesBeforeDisposalAndEscapesText()
        {
            string directory = Path.Combine(Path.GetTempPath(), "pc-journal-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var journal = new PointCloudDiagnosticJournal(directory, "run", "{\"version\":1}", warning => Assert.Fail(warning)))
                {
                    journal.Write("gate", "{\"message\":" + PointCloudDiagnosticJournal.Quote("a\"b\nc\\d\u0001") + "}");
                    string[] lines = File.ReadAllLines(journal.CurrentPath);
                    Assert.That(lines, Has.Length.EqualTo(2));
                    Assert.That(lines[0], Does.Contain("\"event\":\"run_header\""));
                    Assert.That(lines[1], Does.Contain("a\\\"b\\nc\\\\d\\u0001"));
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void RotationIsBoundedAndKeepsHeaderWithoutRemovingOtherFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "pc-journal-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string unrelated = Path.Combine(directory, "unrelated.txt");
            File.WriteAllText(unrelated, "keep");
            try
            {
                using (var journal = new PointCloudDiagnosticJournal(directory, "run", "{\"version\":1}", warning => Assert.Fail(warning), 1024, 3))
                    for (int i = 0; i < 12; i++)
                        journal.Write("frame", "{\"padding\":\"" + new string('x', 500) + "\"}");
                string[] files = Directory.GetFiles(directory, "pointcloud-*.jsonl");
                Assert.That(files.Length, Is.LessThanOrEqualTo(3));
                Assert.That(files.All(file => File.ReadLines(file).First().Contains("run_header")), Is.True);
                Assert.That(File.ReadAllText(unrelated), Is.EqualTo("keep"));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void UnwritableJournalWarnsOnceAndNeverThrowsIntoLocalization()
        {
            string file = Path.GetTempFileName();
            try
            {
                int warnings = 0;
                using (var journal = new PointCloudDiagnosticJournal(file, "run", "{}", _ => warnings++))
                {
                    Assert.DoesNotThrow(() => journal.Write("gate", "{}"));
                    Assert.DoesNotThrow(() => journal.Write("gate", "{}"));
                }
                Assert.That(warnings, Is.EqualTo(1));
            }
            finally { File.Delete(file); }
        }
    }
}
