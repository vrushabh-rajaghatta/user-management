namespace Ligature.Platform.Application.Audit;

/// <summary>
/// Writes records that must survive the command they came from.
///
/// The transactional writer enlists in the command's transaction, so what it
/// writes shares the command's fate. That is right for a record of something
/// that happened, and wrong for a record of something that FAILED: a
/// rejected token, a refused sign-in. Those must be recorded precisely when
/// the thing they describe did not succeed, and a record on the failing
/// transaction is rolled back exactly when it is needed.
///
/// So this one owns a connection and a transaction of its own, and commits
/// independently. Nothing it writes can be undone by the command, and the
/// command cannot be undone by it.
///
/// Internal, like the transactional writer: no handler holds either
/// (IMPL-02). Handlers declare; the pipeline decides which writer a
/// declaration goes to, and the catalogue's write path decides for it.
/// </summary>
internal interface IAutonomousAuditRecordWriter
{
    Task WriteAsync(
        IReadOnlyList<AuditRecordRow> rows,
        CancellationToken cancellationToken);
}
