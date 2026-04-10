using System.Linq;

namespace HapticFeedback;

public class HapticFeedback : IHapticFeedback
{
    public void Click()
    {
        TriggerImpact(UIImpactFeedbackStyle.Light);
    }

    public void LongPress()
    {
        TriggerImpact(UIImpactFeedbackStyle.Medium);
    }

    private static void TriggerImpact(UIImpactFeedbackStyle style)
    {
        var view = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow)?
            .RootViewController?
            .View;
        if (view is null)
        {
            return;
        }

        using var generator = UIImpactFeedbackGenerator.GetFeedbackGenerator(style, view);
        generator.Prepare();
        generator.ImpactOccurred();
    }
}
