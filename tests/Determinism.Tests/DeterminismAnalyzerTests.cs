using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace StickFighter.Determinism.Tests;

public class DeterminismAnalyzerTests
{
    [Theory]
    [InlineData("struct S { float X; }")]
    [InlineData("struct S { public double X { get; set; } }")]
    [InlineData("class C { int M(float f) => 0; }")]
    [InlineData("class C { double M() => 0; }")]
    [InlineData("class C { void M() { var x = 1.5; } }")]
    [InlineData("class C { int M(int a) => (int)(a * 0.5); }")]
    [InlineData("class C { void M() { decimal d = 1; } }")]
    [InlineData("class C { float[] A = new float[4]; }")]
    [InlineData("class C { System.Collections.Generic.List<double> L = new(); }")]
    public async Task FlagsFloatingPoint(string source)
    {
        var ids = await AnalyzeAsync(source);
        Assert.Contains("SF0001", ids);
    }

    [Theory]
    [InlineData("var d = new Dictionary<int, int>(); foreach (var kv in d) { }")]
    [InlineData("var d = new Dictionary<int, int>(); foreach (var k in d.Keys) { }")]
    [InlineData("var d = new Dictionary<int, int>(); foreach (var v in d.Values) { }")]
    [InlineData("var s = new HashSet<int>(); foreach (var x in s) { }")]
    public async Task FlagsUnorderedIteration(string body)
    {
        var ids = await AnalyzeAsync(Wrap(body));
        Assert.Contains("SF0002", ids);
    }

    [Theory]
    [InlineData("struct S { int X; long Y; }")]
    [InlineData("class C { int M(int a, int b) => a * b / 2; }")]
    [InlineData("class C { void M() { var a = new int[4]; foreach (var x in a) { } } }")]
    [InlineData("class C { void M() { var d = new System.Collections.Generic.Dictionary<int, int>(); var y = d[1]; } }")]
    public async Task AllowsIntegerAndOrderedCode(string source)
    {
        var ids = await AnalyzeAsync(source);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task PragmaSuppressesWithJustification()
    {
        var ids = await AnalyzeAsync("""
            class C
            {
            #pragma warning disable SF0001 // display only, never read by the simulation
                float X;
            #pragma warning restore SF0001
            }
            """);
        Assert.Empty(ids);
    }

    private static string Wrap(string body) =>
        "using System.Collections.Generic; class C { void M() { " + body + " } }";

    private static async Task<string[]> AnalyzeAsync(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Probe",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.ToString())));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DeterminismAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(d => d.Id).Distinct().ToArray();
    }
}
