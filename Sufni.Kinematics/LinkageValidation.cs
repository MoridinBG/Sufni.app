namespace Sufni.Kinematics;

public enum LinkageValidationErrorCode
{
    DuplicateJointName,
    MissingLinkJoint,
    MissingShockJoint,
    DuplicateLink,
    DegenerateLink,
    DegenerateShock,
    MissingRequiredJointName,
}

public sealed class LinkageValidationException : Exception
{
    public LinkageValidationException(LinkageValidationErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public LinkageValidationErrorCode Code { get; }
}
