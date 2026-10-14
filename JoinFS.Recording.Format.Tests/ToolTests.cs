using JoinFS.Recording.Tool;

namespace JoinFS.Recording.Format.Tests;

/// <summary>The <c>jfs2</c> command-line tool (dump, verify, repair) on the spec's example file.</summary>
public sealed class ToolTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("jfs2-tool-").FullName;
    static readonly string Example = Path.Combine(AppContext.BaseDirectory, "Fixtures", "example.jfs2");

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = Program.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Verify_AcceptsAFinishedFile()
    {
        var (code, output, _) = Run("verify", Example);

        Assert.Equal(0, code);
        Assert.StartsWith("OK: finished", output.TrimEnd());
    }

    [Fact]
    public void Verify_ReportsACorruptChunk()
    {
        string damaged = Path.Combine(folder, "damaged.jfs2");
        byte[] bytes = File.ReadAllBytes(Example);
        long at;
        using (var reader = RecordingReader.Open(Example))
        {
            at = reader.Chunks[0].Offset + 20;
        }
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(damaged, bytes);

        var (code, output, _) = Run("verify", damaged);

        Assert.Equal(1, code);
        Assert.Contains("chunk #0", output);
        Assert.Contains("NOT OK", output);
    }

    [Fact]
    public void Repair_FinishesACutOffFile_AndLeavesTheOriginalAlone()
    {
        byte[] bytes = File.ReadAllBytes(Example);
        string cut = Path.Combine(folder, "cut.jfs2");
        int length;
        using (var reader = RecordingReader.Open(Example))
        {
            length = (int)reader.Chunks[0].Offset + 60; // cut inside the first chunk
        }
        File.WriteAllBytes(cut, bytes[..length]);

        var (code, output, _) = Run("repair", cut);

        Assert.Equal(0, code);
        Assert.Equal(length, new FileInfo(cut).Length);
        string repaired = Path.Combine(folder, "cut-repaired.jfs2");
        Assert.True(File.Exists(repaired), output);
        Assert.Equal(0, Run("verify", repaired).Code);
    }

    [Fact]
    public void Repair_DoesNotOverwriteAnExistingOutput()
    {
        string existing = Path.Combine(folder, "out.jfs2");
        File.WriteAllText(existing, "keep");

        var (code, _, error) = Run("repair", Example, "-o", existing);

        Assert.Equal(1, code);
        Assert.Contains("exists", error);
        Assert.Equal("keep", File.ReadAllText(existing));
    }

    [Fact]
    public void Dump_ListsTheStructure()
    {
        var (code, output, _) = Run("dump", Example);

        Assert.Equal(0, code);
        Assert.Contains("JFS2 edition 1.0, finished", output);
        Assert.Contains("chunks", output);
    }

    [Fact]
    public void ANonJfs2File_IsAnErrorNotACrash()
    {
        string text = Path.Combine(folder, "text.jfs2");
        File.WriteAllText(text, "this is not a recording");

        var (code, _, error) = Run("verify", text);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void NoCommand_PrintsTheUsage()
    {
        var (code, _, error) = Run();

        Assert.Equal(2, code);
        Assert.Contains("Usage", error);
    }
}
