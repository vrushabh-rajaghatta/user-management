namespace Ligature.Platform.Application.Audit;

/// <summary>
/// The single writer (IMPL-02). Inserts resolved rows into
/// audit.audit_record and audit.audit_entity_ref on the ambient transaction,
/// records then references (AE8), in the order given.
///
/// INTERNAL, deliberately. Handlers live in this assembly, so an interface
/// they could inject is an interface they could call — and anything a
/// handler could call, a handler could omit. The pipeline's emission
/// behaviour is the only consumer, an architecture test asserts that no
/// handler references this type, and Persistence implements it through
/// InternalsVisibleTo rather than through a public seam.
/// </summary>
internal interface IAuditRecordWriter
{
    /// <summary>
    /// Requires a transaction to already be open on the scope's connection;
    /// throws if none is, because a write outside the command's transaction
    /// is a write that could commit when the command does not (AUD-4).
    /// </summary>
    Task WriteAsync(
        IReadOnlyList<AuditRecordRow> rows,
        CancellationToken cancellationToken);
}
