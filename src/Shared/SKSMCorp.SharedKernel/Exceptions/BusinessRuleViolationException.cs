namespace SKSMCorp.SharedKernel.Exceptions;

public class BusinessRuleViolationException : Exception
{
    public BusinessRuleViolationException(string message)
        : base(message)
    {
    }
}