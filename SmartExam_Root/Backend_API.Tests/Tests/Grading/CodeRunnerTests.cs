using System.Text;
using Backend_API.Services.Grading;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>
/// M11 — the code runners. Harness generation / parsing are pure; the real-runner tests compile and run test programs
/// on this machine and skip themselves (with the reason) where Docker + gcc:13 or MinGW g++ is missing.
/// </summary>
public class CodeRunnerTests
{
    private static readonly CodeJobLimits Limits = new(10, 256, 64 * 1024);

    private static CodeJob Job(string language, string source, params (string Input, int LimitMs)[] cases) =>
        new(language, source, cases.Select(c => new CodeJobCase(c.Input, c.LimitMs)).ToList(), Limits);

    private const string SumCpp = "#include <iostream>\nint main(){ long long a,b; std::cin>>a>>b; std::cout<<a+b<<\"\\n\"; }\n";
    private const string LoopForever = "#include <iostream>\nint main(){ int n; std::cin>>n; if(n==0){ volatile int x=0; for(;;) x++; } std::cout<<n; }\n";

    // ── Pure: harness + parsing ─────────────────────────────────────────────

    [Fact]
    public void Harness_CompilesCpp17Unoptimised_AndRunsEachCaseWithItsLimit()
    {
        var harness = DockerCodeRunner.BuildHarness(Job(CodeLanguages.Cpp, "x", ("1", 2000), ("2", 500)), "main.cpp");
        harness.Should().Contain("g++ -std=c++17 -O0").And.Contain("run_case 0 2.000\n").And.Contain("run_case 1 0.500\n");
        harness.Should().NotContain("\r", "the script runs under sh in Linux");
        DockerCodeRunner.BuildHarness(Job(CodeLanguages.C, "x", ("1", 2000)), "main.c").Should().Contain("gcc -std=c11 -O0").And.Contain("-lm");
    }

    [Fact]
    public void Parse_CompileFailure()
    {
        var output = "COMPILE_FAILED " + Convert.ToBase64String(Encoding.UTF8.GetBytes("main.cpp:1: error: boom"));
        var report = DockerCodeRunner.Parse(output, Job(CodeLanguages.Cpp, "x", ("", 2000)), "docker", 5)!;
        report.Compiled.Should().BeFalse();
        report.CompileOutput.Should().Contain("error: boom");
        report.Cases.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Cases_TimeoutMemoryAndOutput()
    {
        var job = Job(CodeLanguages.Cpp, "x", ("1", 2000), ("2", 2000), ("3", 2000), ("4", 2000));
        var five = Convert.ToBase64String(Encoding.UTF8.GetBytes("5\n"));
        var output = string.Join("\n",
            "COMPILED ",
            $"CASE 0 0 12 0 {five}",
            "CASE 1 124 2001 0 ",       // TERM on timeout
            "CASE 2 137 300 1 ",        // cgroup recorded an OOM kill
            "CASE 3 137 40 0 ");        // SIGKILL well inside the time limit: the OOM killer (cgroup v1 host)
        var report = DockerCodeRunner.Parse(output, job, "docker", 5)!;

        report.Compiled.Should().BeTrue();
        report.CompileOutput.Should().BeNull();
        report.Cases[0].Should().BeEquivalentTo(new { Stdout = "5\n", ExitCode = 0, TimedOut = false, MemoryExceeded = false });
        report.Cases[1].Should().BeEquivalentTo(new { TimedOut = true, MemoryExceeded = false });
        report.Cases[2].Should().BeEquivalentTo(new { TimedOut = false, MemoryExceeded = true });
        report.Cases[3].MemoryExceeded.Should().BeTrue();
    }

    [Fact]
    public void Parse_NoCompileMarker_MeansTheEngineBroke() =>
        DockerCodeRunner.Parse("docker: Error response from daemon", Job(CodeLanguages.Cpp, "x"), "docker", 5).Should().BeNull();

    [Fact]
    public async Task FakeRunner_Markers()
    {
        var job = new CodeJob(CodeLanguages.Cpp, "int main(){}", [new CodeJobCase("1", 2000, "3")], Limits);
        (await new FakeCodeRunner(0).RunAsync(job, CancellationToken.None)).Cases.Single().Stdout.Should().Be("3");
        (await new FakeCodeRunner(0).RunAsync(job with { Source = "#compile-error" }, CancellationToken.None)).Compiled.Should().BeFalse();
        (await new FakeCodeRunner(0).RunAsync(job with { Source = "#timeout" }, CancellationToken.None)).Cases.Single().TimedOut.Should().BeTrue();
    }

    // ── Real Docker sandbox ─────────────────────────────────────────────────

    private static DockerCodeRunner Docker() =>
        new(Options.Create(new GradingOptions()), NullLogger<DockerCodeRunner>.Instance);

    [DockerFact]
    public async Task Docker_CompilesAndRunsEveryCase()
    {
        var report = await Docker().RunAsync(Job(CodeLanguages.Cpp, SumCpp, ("1 2", 2000), ("40 2", 2000)), CancellationToken.None);
        report.Compiled.Should().BeTrue();
        report.Cases.Select(c => c.Stdout.Trim()).Should().Equal("3", "42");
        report.Cases.Should().OnlyContain(c => c.ExitCode == 0 && !c.TimedOut);
        report.Engine.Should().Contain("isolated");
    }

    [DockerFact]
    public async Task Docker_TimeoutFailsOnlyThatCase()
    {
        var report = await Docker().RunAsync(Job(CodeLanguages.Cpp, LoopForever, ("0", 1000), ("7", 2000)), CancellationToken.None);
        report.Cases[0].TimedOut.Should().BeTrue();
        report.Cases[1].Stdout.Should().Be("7");
    }

    [DockerFact]
    public async Task Docker_CompileError_IsAReport_NotAnException()
    {
        var report = await Docker().RunAsync(Job(CodeLanguages.Cpp, "int main(){ return n; }", ("", 2000)), CancellationToken.None);
        report.Compiled.Should().BeFalse();
        report.CompileOutput.Should().Contain("'n' was not declared");
    }

    // ── Real local MinGW runner (Windows Job Object) ────────────────────────

    private static LocalMingwCodeRunner Local() => new(Options.Create(new GradingOptions()));

    [MingwFact]
    public async Task Local_CompilesStaticallyAndRuns()
    {
        var runner = Local();
        runner.IsAvailable.Should().BeTrue();
        var report = await runner.RunAsync(Job(CodeLanguages.Cpp, SumCpp, ("1 2", 2000), ("-5 3", 2000)), CancellationToken.None);
        report.Compiled.Should().BeTrue(report.CompileOutput);
        report.Cases.Select(c => c.Stdout.Trim()).Should().Equal("3", "-2");
        report.Engine.Should().Contain("not isolated");
    }

    [MingwFact]
    public async Task Local_TimeoutKillsTheProgram_AndTheNextCaseRuns()
    {
        var report = await Local().RunAsync(Job(CodeLanguages.Cpp, LoopForever, ("0", 800), ("9", 2000)), CancellationToken.None);
        report.Cases[0].TimedOut.Should().BeTrue();
        report.Cases[1].Stdout.Should().Be("9");
    }

    [MingwFact]
    public async Task Local_MemoryLimit_IsReported()
    {
        const string hog = "#include <stdlib.h>\n#include <string.h>\n#include <stdio.h>\nint main(){ char* p = malloc(1024L*1024*1024); memset(p, 1, 1024L*1024*1024); printf(\"3\"); }\n";
        var report = await Local().RunAsync(Job(CodeLanguages.C, hog, ("", 2000)), CancellationToken.None);
        report.Compiled.Should().BeTrue(report.CompileOutput);
        report.Cases[0].MemoryExceeded.Should().BeTrue();
        report.Cases[0].Stdout.Should().NotBe("3");
    }

    [MingwFact]
    public async Task Local_ProgramCannotStartChildProcesses_NorSeeServerSecrets()
    {
        const string probe = """
            #include <stdio.h>
            #include <stdlib.h>
            int main(){
              int rc = system("cmd /c exit 0");
              const char* secret = getenv("SMARTEXAM_TEST_SECRET");
              printf("%s %s", rc == 0 ? "spawned" : "blocked", secret ? secret : "none");
            }
            """;
        Environment.SetEnvironmentVariable("SMARTEXAM_TEST_SECRET", "s3cr3t");
        try
        {
            var report = await Local().RunAsync(Job(CodeLanguages.C, probe, ("", 3000)), CancellationToken.None);
            report.Cases[0].Stdout.Should().Be("blocked none");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMARTEXAM_TEST_SECRET", null);
        }
    }

    [MingwFact]
    public async Task Local_CompileError()
    {
        var report = await Local().RunAsync(Job(CodeLanguages.Cpp, "int main(){ return n; }", ("", 2000)), CancellationToken.None);
        report.Compiled.Should().BeFalse();
        report.CompileOutput.Should().Contain("'n' was not declared");
    }
}
