using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.LinkageEditing;

public class LinkageDraftTests
{
    [Fact]
    public void LoadedLinkageRetainsItsMillimetreGeometryAndNamedEndpoints()
    {
        var source = TestSnapshots.FullSuspensionLinkageSpec(true);
        var draft = LinkageSpecMapping.FromSpec(source, 75, 0.5);

        var wheel = draft.Joints.Single(joint => joint.Type == JointType.FrontWheel);
        Assert.Equal(24, wheel.X);
        Assert.Equal(73, wheel.Y);
        Assert.Equal(source, LinkageSpecMapping.ToSpec(draft, 75, 0.5, source.ShockStroke));
    }

    [Fact]
    public void DeletingAnEditableJointRemovesItsLinksAndPreservesTheShock()
    {
        var seeded = LinkageIntentReducers.Seed();
        var draft = LinkageIntentReducers.AddJoint(seeded);
        var jointId = draft.Joints[^1].Id;
        draft = LinkageIntentReducers.AddLink(draft);
        var linkId = draft.Links[^1].Id;
        draft = LinkageIntentReducers.SetLinkEnd(draft, linkId, true, jointId);

        var result = LinkageIntentReducers.Delete(draft, jointId);
        Assert.Null(result.Joint(jointId));
        Assert.Null(result.Link(linkId));
        Assert.Equal(seeded.Link(seeded.ShockId), result.Link(result.ShockId));
    }

    [Fact]
    public void ShockEyeTypeCanChangeWhileItsNameAndShockEndpointsStayFixed()
    {
        var draft = LinkageIntentReducers.Seed();
        var shock = draft.Link(draft.ShockId)!;
        var eye = draft.Joint(shock.A)!;

        var edited = LinkageIntentReducers.Rename(draft, eye.Id, "pivot");
        edited = LinkageIntentReducers.SetType(edited, eye.Id, JointType.Fixed);
        edited = LinkageIntentReducers.SetLinkEnd(edited, shock.Id, true, null);

        Assert.Equal(eye with { Type = JointType.Fixed }, edited.Joint(eye.Id));
        Assert.Equal(shock, edited.Link(shock.Id));
        Assert.False(LinkageIntentReducers.CanDelete(edited, eye.Id));
    }
}
