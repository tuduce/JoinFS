using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// BenchmarkDotNet's default toolchain generates and builds an isolated copy of this project per
// run - but that generated project doesn't know JoinFS.csproj needs Configuration=CONSOLE (its
// custom build configurations aren't NuGet-package-style versioned, so BenchmarkDotNet's usual
// "restore the same references" approach can't infer it), so its restore fails. Run in-process
// instead: this exe is already built correctly (JoinFS.Benchmarks.csproj's own ProjectReference),
// so there's nothing to regenerate.
IConfig config = DefaultConfig.Instance.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

partial class Program;
