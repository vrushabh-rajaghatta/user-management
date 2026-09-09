namespace Ligature.Platform.Application.Audit;

/// <summary>
/// What a handler injects to declare the audit events its catalogue entry
/// names. Declaring is all it can do.
///
/// There is deliberately no way to write through this interface, no way to
/// see or alter the actor snapshot, and no way to reach the writer
/// (docs/architecture.md section 6, IMPL-02). A handler that could write
/// audit rows could also forget to; a handler that can only declare leaves
/// the writing to the pipeline, which cannot omit itself. Behaviour 7 reads
/// what was declared once the handler returns, still inside the command's
/// transaction, and does the rest.
/// </summary>
public interface IAuditEvents
{
    /// <summary>
    /// Declares one event by its verbatim catalogue code and version, and
    /// returns it for the handler to complete with the primary entity,
    /// references, content and reason.
    /// </summary>
    AuditEventDeclaration Emit(string code, int version);
}
