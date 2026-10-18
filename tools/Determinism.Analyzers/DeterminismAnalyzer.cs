using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace StickFighter.Determinism;

// Fails the build on floating-point values and unordered iteration in simulation code.
// A justified exception is written as `#pragma warning disable SF0001 // why`.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor FloatingPoint = new(
        id: "SF0001",
        title: "Floating-point in simulation code",
        messageFormat: "'{0}' is floating-point; simulation code uses integers or Fix",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnorderedIteration = new(
        id: "SF0002",
        title: "Unordered iteration in simulation code",
        messageFormat: "Iterating '{0}' has no defined order; use an array or a list",
        category: "Determinism",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly string[] UnorderedTypes =
    {
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Dictionary`2+KeyCollection",
        "System.Collections.Generic.Dictionary`2+ValueCollection",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
        "System.Collections.Concurrent.ConcurrentBag`1",
        "System.Collections.Hashtable",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(FloatingPoint, UnorderedIteration);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeDeclaration,
            SymbolKind.Field, SymbolKind.Property, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeOperation,
            OperationKind.Literal, OperationKind.LocalReference, OperationKind.FieldReference,
            OperationKind.PropertyReference, OperationKind.ParameterReference,
            OperationKind.Invocation, OperationKind.Conversion, OperationKind.Binary,
            OperationKind.Unary, OperationKind.ObjectCreation, OperationKind.VariableDeclarator);
        context.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
    }

    private static void AnalyzeDeclaration(SymbolAnalysisContext context)
    {
        switch (context.Symbol)
        {
            case IFieldSymbol field when IsFloatingPoint(field.Type):
                Report(context, field, field.Type);
                break;
            case IPropertySymbol property when IsFloatingPoint(property.Type):
                Report(context, property, property.Type);
                break;
            case IMethodSymbol method:
                if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                    break;
                if (IsFloatingPoint(method.ReturnType))
                    Report(context, method, method.ReturnType);
                foreach (var parameter in method.Parameters.Where(p => IsFloatingPoint(p.Type)))
                    Report(context, parameter, parameter.Type);
                break;
        }
    }

    private static void Report(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type)
    {
        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
            context.ReportDiagnostic(Diagnostic.Create(FloatingPoint, location, type.ToDisplayString()));
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        var type = operation is IVariableDeclaratorOperation declarator
            ? declarator.Symbol.Type
            : operation.Type;
        if (!IsFloatingPoint(type))
            return;

        // Report each expression once, at its outermost floating-point node.
        if (operation.Parent is { } parent && IsFloatingPoint(parent.Type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            FloatingPoint, operation.Syntax.GetLocation(), type!.ToDisplayString()));
    }

    private static void AnalyzeForEach(OperationAnalysisContext context)
    {
        if (context.Operation is not IForEachLoopOperation loop)
            return;

        var collection = loop.Collection is IConversionOperation conversion
            ? conversion.Operand
            : loop.Collection;
        if (collection.Type is not INamedTypeSymbol type || !IsUnordered(type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            UnorderedIteration, collection.Syntax.GetLocation(), type.ToDisplayString()));
    }

    private static bool IsFloatingPoint(ITypeSymbol? type) =>
        type is not null && (
            type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
            || type is IArrayTypeSymbol array && IsFloatingPoint(array.ElementType)
            || type is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Any(IsFloatingPoint));

    private static bool IsUnordered(INamedTypeSymbol type)
    {
        var name = MetadataName(type.OriginalDefinition);
        return UnorderedTypes.Contains(name);
    }

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingType is { } outer
            ? MetadataName(outer) + "+" + type.MetadataName
            : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
}
