using System;
using Sufni.App.ViewModels.LinkageParts;

namespace Sufni.App.ViewModels.LinkageEditing;

public sealed class LinkageEditorChange(
    LinkageEditorChangeKind kind,
    JointViewModel? joint = null,
    LinkViewModel? link = null) : EventArgs
{
    public LinkageEditorChangeKind Kind { get; } = kind;
    public JointViewModel? Joint { get; } = joint;
    public LinkViewModel? Link { get; } = link;
}