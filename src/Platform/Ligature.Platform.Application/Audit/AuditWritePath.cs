namespace Ligature.Platform.Application.Audit;

/// <summary>
/// The two ways a record reaches the trail, as the catalogue names them
/// (ET6).
///
/// Transactional records are written inside the command's transaction and
/// commit with it: if the business change is rolled back, so is the record,
/// and the trail never describes something that did not happen.
///
/// Autonomous records commit independently, because the event they describe
/// is one where there IS no successful command to enlist in — a rejected
/// token, a failed sign-in, a refused authorisation. Those must be recorded
/// precisely when the thing they record has failed.
///
/// The distinction is the catalogue's to make, not the pipeline's. Every
/// emission path states which of the two it can honour and the validator
/// refuses a mismatch, so an event declared one way cannot be quietly
/// written the other. Until the autonomous writer exists, declaring an
/// autonomous event is a defect rather than a silently transactional write.
/// </summary>
public static class AuditWritePath
{
    public const string Transactional = "Transactional";

    public const string Autonomous = "Autonomous";
}
