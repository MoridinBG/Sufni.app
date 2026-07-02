namespace Sufni.Kinematics;

public static class LinkageResolver
{
    private const double DegenerateLengthTolerance = 1e-12;

    public static ResolvedLinkage Resolve(LinkageSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        ValidateRequiredNames(spec);
        var jointsByName = BuildJointMap(spec);
        var links = ResolveLinks(spec, jointsByName);
        var shock = ResolveShock(spec.Shock, jointsByName);

        return new ResolvedLinkage(spec, jointsByName.Values.ToArray(), links, shock);
    }

    private static void ValidateRequiredNames(LinkageSpec spec)
    {
        if (spec.Joints.Any(joint => string.IsNullOrWhiteSpace(joint.Name)) ||
            spec.Links.Any(link => string.IsNullOrWhiteSpace(link.A) || string.IsNullOrWhiteSpace(link.B)) ||
            string.IsNullOrWhiteSpace(spec.Shock.A) ||
            string.IsNullOrWhiteSpace(spec.Shock.B))
        {
            throw new LinkageValidationException(
                LinkageValidationErrorCode.MissingRequiredJointName,
                "All joints and links must reference named joints.");
        }
    }

    private static Dictionary<string, ResolvedJoint> BuildJointMap(LinkageSpec spec)
    {
        Dictionary<string, ResolvedJoint> jointsByName = new(StringComparer.Ordinal);
        foreach (var joint in spec.Joints)
        {
            if (jointsByName.ContainsKey(joint.Name))
            {
                throw new LinkageValidationException(
                    LinkageValidationErrorCode.DuplicateJointName,
                    $"Duplicate joint name '{joint.Name}'.");
            }

            jointsByName.Add(joint.Name, new ResolvedJoint(joint.Name, joint.Type, joint.X, joint.Y));
        }

        return jointsByName;
    }

    private static ResolvedLink[] ResolveLinks(
        LinkageSpec spec,
        IReadOnlyDictionary<string, ResolvedJoint> jointsByName)
    {
        HashSet<LinkKey> seenLinks = [];
        List<ResolvedLink> links = [];
        foreach (var link in spec.Links)
        {
            if (!jointsByName.TryGetValue(link.A, out var a) ||
                !jointsByName.TryGetValue(link.B, out var b))
            {
                throw new LinkageValidationException(
                    LinkageValidationErrorCode.MissingLinkJoint,
                    $"Link '{link.A}'-'{link.B}' references a missing joint.");
            }

            if (!seenLinks.Add(LinkKey.Create(link.A, link.B)))
            {
                throw new LinkageValidationException(
                    LinkageValidationErrorCode.DuplicateLink,
                    $"Duplicate link '{link.A}'-'{link.B}'.");
            }

            if (ResolvedLink.CalculateLength(a, b) < DegenerateLengthTolerance)
            {
                throw new LinkageValidationException(
                    LinkageValidationErrorCode.DegenerateLink,
                    $"Link '{link.A}'-'{link.B}' has zero length.");
            }

            links.Add(new ResolvedLink(link.A, link.B, a, b));
        }

        return [.. links];
    }

    private static ResolvedLink ResolveShock(
        LinkSpec shock,
        IReadOnlyDictionary<string, ResolvedJoint> jointsByName)
    {
        if (!jointsByName.TryGetValue(shock.A, out var a) ||
            !jointsByName.TryGetValue(shock.B, out var b))
        {
            throw new LinkageValidationException(
                LinkageValidationErrorCode.MissingShockJoint,
                $"Shock '{shock.A}'-'{shock.B}' references a missing joint.");
        }

        if (ResolvedLink.CalculateLength(a, b) < DegenerateLengthTolerance)
        {
            throw new LinkageValidationException(
                LinkageValidationErrorCode.DegenerateShock,
                $"Shock '{shock.A}'-'{shock.B}' has zero length.");
        }

        return new ResolvedLink(shock.A, shock.B, a, b);
    }

    private readonly record struct LinkKey(string First, string Second)
    {
        public static LinkKey Create(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? new LinkKey(a, b) : new LinkKey(b, a);
    }
}
