using System;
using System.IO;
using Xunit;

namespace JoinFS.Tests
{
    /// <summary>
    /// The built-in variable files are written at every start (#214): an up-to-date file must not be written again, and a write
    /// that Windows refuses must leave the old file whole and no temporary file behind.
    /// </summary>
    public sealed class WriteIfChangedTests : IDisposable
    {
        readonly string folder = Path.Combine(Path.GetTempPath(), "JoinFS-WriteIfChanged-" + Guid.NewGuid().ToString("N"));
        string File1 => Path.Combine(folder, "Plane.txt");

        public WriteIfChangedTests() => Directory.CreateDirectory(folder);

        public void Dispose()
        {
            if (File.Exists(File1)) File.SetAttributes(File1, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }

        [Fact]
        public void MissingFile_IsWritten()
        {
            Assert.True(VariableMgr.WriteIfChanged(File1, "A\r\n"));
            Assert.Equal("A\r\n", File.ReadAllText(File1));
        }

        [Fact]
        public void SameText_IsNotWrittenAgain()
        {
            File.WriteAllText(File1, "A\r\n");
            // read-only: any write attempt would throw
            File.SetAttributes(File1, FileAttributes.ReadOnly);

            Assert.False(VariableMgr.WriteIfChanged(File1, "A\r\n"));
        }

        [Fact]
        public void ChangedText_ReplacesTheFile()
        {
            File.WriteAllText(File1, "A\r\n");

            Assert.True(VariableMgr.WriteIfChanged(File1, "B\r\n"));
            Assert.Equal("B\r\n", File.ReadAllText(File1));
            Assert.Single(Directory.GetFiles(folder));
        }

        [Fact]
        public void RefusedWrite_Throws_AndKeepsTheOldFile()
        {
            File.WriteAllText(File1, "A\r\n");
            File.SetAttributes(File1, FileAttributes.ReadOnly);

            Assert.Throws<UnauthorizedAccessException>(() => VariableMgr.WriteIfChanged(File1, "B\r\n"));
            Assert.Equal("A\r\n", File.ReadAllText(File1));
            Assert.Single(Directory.GetFiles(folder));
        }
    }
}
