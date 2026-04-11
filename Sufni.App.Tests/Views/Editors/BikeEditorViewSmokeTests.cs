using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Views.Editors;

public class BikeEditorViewSmokeTests
{
    [AvaloniaFact]
    public async Task BikeEditorView_ShowsWithPopulatedBikeEditorViewModel()
    {
        EnsureBikeEditorViewResources();
        EnsurePlotViewStyle();

        var view = new BikeEditorView
        {
            DataContext = CreateViewModel()
        };
        var host = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        host.Show();
        await FlushUiAsync();

        var bikeImage = view.FindControl<Image>("BikeImage");

        Assert.NotNull(bikeImage);
        Assert.NotNull(bikeImage.Source);

        host.Close();
        await FlushUiAsync();
    }

    [AvaloniaFact]
    public async Task BikeEditorDesktopView_ShowsWithPopulatedBikeEditorViewModel()
    {
        EnsureBikeEditorViewResources();
        EnsurePlotViewStyle();

        var view = new BikeEditorDesktopView
        {
            DataContext = CreateViewModel()
        };
        var host = new Window
        {
            Width = 1200,
            Height = 800,
            Content = view
        };

        host.Show();
        await FlushUiAsync();

        Assert.Single(view.GetVisualDescendants().OfType<SplitView>());

        host.Close();
        await FlushUiAsync();
    }

    private static BikeEditorViewModel CreateViewModel()
    {
        var bikeCoordinator = Substitute.For<IBikeCoordinator>();
        bikeCoordinator.LoadAnalysisAsync(Arg.Any<Linkage?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        bikeCoordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        bikeCoordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        bikeCoordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));

        var dependencyQuery = Substitute.For<IBikeDependencyQuery>();
        dependencyQuery.Changes.Returns(Observable.Empty<Unit>());
        dependencyQuery.IsBikeInUse(Arg.Any<Guid>()).Returns(false);

        return new BikeEditorViewModel(
            CreateSnapshot(),
            isNew: false,
            bikeCoordinator,
            dependencyQuery,
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>());
    }

    private static BikeSnapshot CreateSnapshot()
    {
        var bike = new Bike(Guid.NewGuid(), "view bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = 0.5,
            Chainstay = 440,
            PixelsToMillimeters = 1,
            Linkage = CreateFullSuspensionLinkage(includeHeadTubeJoints: true),
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch29, 2.4),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch275, 2.5),
            ImageRotationDegrees = 12.5,
            Image = TestImages.SmallPng(),
            Updated = 1,
        };

        return BikeSnapshot.From(bike);
    }

    private static Linkage CreateFullSuspensionLinkage(bool includeHeadTubeJoints = false)
    {
        var mapping = new JointNameMapping();
        var bottomBracket = new Joint(mapping.BottomBracket, JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint(mapping.RearWheel, JointType.RearWheel, 4, 0);
        var frontWheel = new Joint(mapping.FrontWheel, JointType.FrontWheel, 12, 1);
        var shockEye1 = new Joint(mapping.ShockEye1, JointType.Floating, 4, 3);
        var shockEye2 = new Joint(mapping.ShockEye2, JointType.Fixed, 0, 3);

        List<Joint> joints = [bottomBracket, rearWheel, frontWheel, shockEye1, shockEye2];
        if (includeHeadTubeJoints)
        {
            joints.Add(new Joint(mapping.HeadTube1, JointType.HeadTube, 10, 2));
            joints.Add(new Joint(mapping.HeadTube2, JointType.HeadTube, 9, 5));
        }

        var linkage = new Linkage
        {
            Joints = [.. joints],
            Links =
            [
                new Link(bottomBracket, rearWheel),
                new Link(rearWheel, shockEye1),
            ],
            Shock = new Link(shockEye1, shockEye2),
            ShockStroke = 0.5,
        };
        linkage.ResolveJoints();
        return linkage;
    }

    private static double WheelDiameter(EtrtoRimSize rimSize, double tireWidth) =>
        Math.Round(rimSize.CalculateTotalDiameterMm(tireWidth), 1);

    private static async Task FlushUiAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static void EnsureBikeEditorViewResources()
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");

        resources["SufniAccentColor"] = Brushes.CornflowerBlue;
        resources["SufniRegion"] = Brushes.Gray;
        resources["SufniForeground"] = Brushes.White;
        resources["SufniBackgroundDisabled"] = Brushes.DimGray;
        resources["SufniBorderBrush"] = Brushes.Black;
        resources["SufniDangerColor"] = Brushes.Red;
        resources["SufniDangerColorDark"] = Brushes.DarkRed;
    }

    private static void EnsurePlotViewStyle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://Sufni.App/Views/Plots/SufniPlotView.axaml");

        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://Sufni.App/"))
        {
            Source = source
        });
    }
}