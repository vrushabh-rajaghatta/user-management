using System.Reflection;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Behaviors;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// IMPL-02, asserted structurally. The writer is internal to the pipeline
/// assembly, and handlers live in that same assembly — so "handlers do not
/// reference the writer" is a discipline the compiler cannot enforce. This
/// test enforces it: every command handler's constructor, fields and public
/// surface are inspected, and none may take, hold or return the writer or
/// the pipeline-side emission scope. A handler may inject IAuditEvents, the
/// declaration side, and nothing more.
///
/// The same technique the execution-context tests use to assert that the
/// read interface has no mutator: a rule that could be broken silently is
/// a rule a test must watch.
/// </summary>
public sealed class AuditWriterIsolationTests
{
    private static readonly Type[] Forbidden =
    [
        typeof(IAuditRecordWriter),
        typeof(IAuditEmissionScope),
        typeof(ScopedAuditEvents),
        typeof(AuditRecordAssembler),
    ];

    [Fact]
    public void No_command_handler_references_the_writer_or_the_emission_scope()
    {
        var handlers = typeof(AuditEmissionBehavior<,>).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .ToList();

        Assert.NotEmpty(handlers);

        foreach (var handler in handlers)
        {
            var referenced = handler
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
                .Concat(handler.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType))
                .Concat(handler.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(p => p.PropertyType))
                .Distinct()
                .ToList();

            foreach (var forbidden in Forbidden)
            {
                Assert.False(
                    referenced.Contains(forbidden),
                    $"{handler.Name} references {forbidden.Name}. Handlers declare "
                    + "events through IAuditEvents; only the pipeline writes them (IMPL-02).");
            }
        }
    }

    [Fact]
    public void The_writer_and_the_emission_scope_are_not_public()
    {
        Assert.False(typeof(IAuditRecordWriter).IsPublic);
        Assert.False(typeof(IAuditEmissionScope).IsPublic);
        Assert.False(typeof(AuditEmissionBehavior<,>).IsPublic);
    }

    [Fact]
    public void The_declaration_surface_exposes_only_Emit()
    {
        var members = typeof(IAuditEvents)
            .GetMembers()
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(["Emit"], members);
    }
}
