using System;

namespace Sufni.App.Infrastructure;

public interface ILayoutProfileTransitionState
{
    bool IsTransitioning { get; }
    IDisposable BeginTransition();
}

public sealed class LayoutProfileTransitionState : ILayoutProfileTransitionState
{
    private int transitionDepth;

    public bool IsTransitioning => transitionDepth > 0;

    public IDisposable BeginTransition()
    {
        transitionDepth++;
        return new TransitionScope(this);
    }

    private void EndTransition()
    {
        if (transitionDepth > 0)
        {
            transitionDepth--;
        }
    }

    private sealed class TransitionScope(LayoutProfileTransitionState owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.EndTransition();
        }
    }
}
