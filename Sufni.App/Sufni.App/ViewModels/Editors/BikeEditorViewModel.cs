using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Sufni.App.BikeEditing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Editor view model for a bike. Created by <c>BikeCoordinator</c>
/// from a <see cref="BikeSnapshot"/>; the snapshot's <c>Updated</c>
/// value is kept as <see cref="BaselineUpdated"/> for optimistic
/// conflict detection at save time.
/// </summary>
public partial class BikeEditorViewModel : TabPageViewModelBase, IEditorActions
{
    public Guid Id { get; private set; }
    public long BaselineUpdated { get; private set; }
    public bool IsInDatabase { get; private set; }

    // Explicit interface implementation: the generated commands are
    // IAsyncRelayCommand[<T>] which C# does not implicitly satisfy a
    // non-generic IRelayCommand interface property with.
    IRelayCommand IEditorActions.OpenPreviousPageCommand => OpenPreviousPageCommand;
    IRelayCommand IEditorActions.SaveCommand => SaveCommand;
    IRelayCommand IEditorActions.ResetCommand => ResetCommand;
    IRelayCommand IEditorActions.DeleteCommand => DeleteCommand;
    IRelayCommand IEditorActions.FakeDeleteCommand => FakeDeleteCommand;

    #region Private fields

    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly IBikeDependencyQuery? dependencyQuery;
    private Bike bike;
    private uint pointNumber = 1;
    private LinkViewModel? shockViewModel;
    private CancellationTokenSource? replaceableWorkflowCts;
    private readonly Dictionary<JointViewModel, PropertyChangedEventHandler> jointPropertyChangedHandlers = [];
    private readonly Dictionary<LinkViewModel, PropertyChangedEventHandler> linkPropertyChangedHandlers = [];

    #endregion Private fields

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? headAngle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? forksStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? shockStroke;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private Bitmap? image;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private double? chainstay;

    [ObservableProperty] private double? pixelsToMillimeters;

    public ObservableCollection<JointViewModel> JointViewModels { get; } = [];
    public ObservableCollection<LinkViewModel> LinkViewModels { get; } = [];
    [ObservableProperty] private JointViewModel? selectedPoint;
    [ObservableProperty] private LinkViewModel? selectedLink;
    [ObservableProperty] private bool overlayVisible;

    [ObservableProperty] private CoordinateList? leverageRatioData;

    [ObservableProperty] private bool isPlotBusy;

    #endregion Observable properties

    #region Property change handlers

    partial void OnSelectedLinkChanged(LinkViewModel? value)
    {
        if (SelectedLink is null) return;
        ClearSelections();
        SelectedLink.IsSelected = true;
        SelectedPoint = null;
    }

    partial void OnSelectedPointChanged(JointViewModel? value)
    {
        if (SelectedPoint is null) return;
        ClearSelections();
        SelectedPoint.IsSelected = true;
        SelectedLink = null;
    }

    partial void OnChainstayChanged(double? value)
    {
        if (value is null)
        {
            PixelsToMillimeters = null;
        }
        else
        {
            UpdatePixelsToMillimeters();
        }
    }

    partial void OnPixelsToMillimetersChanged(double? value)
    {
        foreach (var link in LinkViewModels)
        {
            link.UpdateLength(PixelsToMillimeters);
        }
    }

    #endregion Property change handlers

    #region Constructors

    public BikeEditorViewModel()
    {
        bikeCoordinator = null;
        dependencyQuery = null;
        bike = new Bike();
        IsInDatabase = false;

        SetupJointsListeners();
        SetupLinksListeners();
    }

    public BikeEditorViewModel(
        BikeSnapshot snapshot,
        bool isNew,
        IBikeCoordinator bikeCoordinator,
        IBikeDependencyQuery dependencyQuery,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.dependencyQuery = dependencyQuery;
        IsInDatabase = !isNew;
        Id = snapshot.Id;
        BaselineUpdated = snapshot.Updated;

        bike = BikeFromSnapshot(snapshot);

        SetupJointsListeners();
        SetupLinksListeners();

        UpdateFromBike();

        // Brand new bike: start with the mandatory joints.
        if (isNew) AddInitialJoints();

        // UpdateFromBike's collection clears fire EvaluateDirtiness while
        // Name/HeadAngle/etc. are still null, leaving IsDirty stale-true.
        // Recompute now that all VM fields are populated. The override
        // only reads fields populated above, so the virtual dispatch is
        // safe here.
        // ReSharper disable once VirtualMemberCallInConstructor
        EvaluateDirtiness();
    }

    #endregion Constructors

    #region Private methods

    private static Bike BikeFromSnapshot(BikeSnapshot snapshot)
    {
        // Order matters: Linkage must be set before ShockStroke, because
        // Bike.ShockStroke's setter writes through to Linkage.ShockStroke
        // and silently no-ops when Linkage is null. Chainstay uses an
        // init-only setter on Bike and is restored explicitly here so
        // UpdateFromBike does not have to fall back to the lazy
        // CalculateChainstay() path — the snapshot is already the
        // canonical view of the bike's chainstay value.
        var b = new Bike(snapshot.Id, snapshot.Name)
        {
            HeadAngle = snapshot.HeadAngle,
            ForkStroke = snapshot.ForkStroke,
            Chainstay = snapshot.Chainstay,
            PixelsToMillimeters = snapshot.PixelsToMillimeters,
            Linkage = snapshot.Linkage,
            ShockStroke = snapshot.ShockStroke,
            Image = snapshot.Image,
            Updated = snapshot.Updated
        };
        return b;
    }

    private Bike ToBike()
    {
        Debug.Assert(HeadAngle is not null);
        Debug.Assert(ForksStroke is not null);

        var newBike = new Bike(Id, Name ?? $"bike {Id}")
        {
            HeadAngle = HeadAngle.Value,
            ForkStroke = ForksStroke,
            Chainstay = Chainstay
        };

        // If we don't have a rear suspension, we can return here
        if (ShockStroke is null) return newBike;

        Debug.Assert(PixelsToMillimeters is not null);
        newBike.ShockStroke = ShockStroke;
        newBike.Image = Image;
        newBike.PixelsToMillimeters = PixelsToMillimeters.Value;
        newBike.Linkage = CreateLinkage();

        return newBike;
    }

    private void AddInitialJoints()
    {
        var mapping = new JointNameMapping();
        JointViewModels.Add(new JointViewModel(mapping.FrontWheel, JointType.FrontWheel, 100, 150));
        JointViewModels.Add(new JointViewModel(mapping.BottomBracket, JointType.BottomBracket, 100, 200));
        JointViewModels.Add(new JointViewModel(mapping.RearWheel, JointType.RearWheel, 100, 100));

        var shockEye1 = new JointViewModel(mapping.ShockEye1, JointType.Floating, 100, 250);
        var shockEye2 = new JointViewModel(mapping.ShockEye2, JointType.Floating, 100, 300);
        JointViewModels.Add(shockEye1);
        JointViewModels.Add(shockEye2);
        shockViewModel = new LinkViewModel(shockEye1, shockEye2, "Shock");
        LinkViewModels.Add(shockViewModel);
    }

    private void UpdatePixelsToMillimeters()
    {
        var bb = JointViewModels.FirstOrDefault(p => p.Type == JointType.BottomBracket);
        var rw = JointViewModels.FirstOrDefault(p => p.Type == JointType.RearWheel);
        if (bb is null || rw is null) return;

        var distance = Math.Sqrt(Math.Pow(rw.X - bb.X, 2) + Math.Pow(rw.Y - bb.Y, 2));
        PixelsToMillimeters = Chainstay / distance;
    }

    private void ClearSelections()
    {
        foreach (var point in JointViewModels)
        {
            point.IsSelected = false;
        }

        foreach (var link in LinkViewModels)
        {
            link.IsSelected = false;
        }
    }

    private Linkage CreateLinkage()
    {
        Debug.Assert(ShockStroke is not null);
        Debug.Assert(Image is not null);
        Debug.Assert(PixelsToMillimeters is not null);
        Debug.Assert(shockViewModel is not null);

        return new Linkage
        {
            Joints = [.. JointViewModels.Select(p => p.ToJoint(Image.Size.Height, PixelsToMillimeters.Value))],
            Links = [.. LinkViewModels.Where(l => l != shockViewModel).Select(l => l.ToLink(Image.Size.Height, PixelsToMillimeters.Value))],
            Shock = shockViewModel.ToLink(Image.Size.Height, PixelsToMillimeters.Value),
            ShockStroke = ShockStroke.Value
        };
    }

    private void UpdateFromBike()
    {
        JointViewModels.Clear();
        LinkViewModels.Clear();
        shockViewModel = null;

        ShockStroke = bike.ShockStroke;
        Image = bike.Image;

        if (bike.Linkage is not null)
        {
            Debug.Assert(bike.Image is not null);

            var jointViewModels = bike.Linkage.Joints.Select(j => JointViewModel.FromJoint(j, bike.Image.Size.Height, bike.PixelsToMillimeters));
            foreach (var jvm in jointViewModels)
            {
                JointViewModels.Add(jvm);
            }

            var linkViewModels = bike.Linkage.Links.Select(l => LinkViewModel.FromLink(l, JointViewModels));
            foreach (var link in linkViewModels)
            {
                LinkViewModels.Add(link);
            }
            shockViewModel = LinkViewModel.FromLink(bike.Linkage.Shock, JointViewModels);
            LinkViewModels.Add(shockViewModel);
            ShockStroke = bike.Linkage.ShockStroke;
            Chainstay = bike.Chainstay; // this also updates PixelsToMillimeters
        }
        else
        {
            Chainstay = bike.Chainstay;
            PixelsToMillimeters = null;
        }

        Id = bike.Id;
        Name = bike.Name;
        HeadAngle = bike.HeadAngle;
        ForksStroke = bike.ForkStroke;
    }

    private CancellationTokenSource StartReplaceableWorkflow()
    {
        CancelReplaceableWorkflow();
        replaceableWorkflowCts = new CancellationTokenSource();
        return replaceableWorkflowCts;
    }

    private void CancelReplaceableWorkflow()
    {
        replaceableWorkflowCts?.Cancel();
        replaceableWorkflowCts = null;
        IsPlotBusy = false;
    }

    private bool IsCurrentWorkflow(CancellationTokenSource workflowCts) =>
        replaceableWorkflowCts == workflowCts && !workflowCts.IsCancellationRequested;

    private void TryCompleteCurrentWorkflow(CancellationTokenSource workflowCts)
    {
        workflowCts.Dispose();

        if (replaceableWorkflowCts != workflowCts) return;

        replaceableWorkflowCts = null;
        IsPlotBusy = false;
    }

    private void QueuePlotRefresh() => _ = RefreshAnalysisAsync(showPlotBusyOverlay: true);

    private async Task RefreshAnalysisAsync(bool showPlotBusyOverlay = false)
    {
        if (bikeCoordinator is null) return;

        var workflowCts = StartReplaceableWorkflow();
        IsPlotBusy = showPlotBusyOverlay;
        try
        {
            var result = await bikeCoordinator.LoadAnalysisAsync(bike.Linkage, workflowCts.Token);
            if (!IsCurrentWorkflow(workflowCts)) return;

            ApplyAnalysisResult(result);
        }
        catch (OperationCanceledException) when (workflowCts.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (!IsCurrentWorkflow(workflowCts)) return;

            ApplyAnalysisResult(new BikeEditorAnalysisResult.Failed(e.Message));
        }
        finally
        {
            TryCompleteCurrentWorkflow(workflowCts);
        }
    }

    private void ApplyAnalysisResult(BikeEditorAnalysisResult result)
    {
        switch (result)
        {
            case BikeEditorAnalysisResult.Computed computed:
                LeverageRatioData = computed.Data.LeverageRatioData;
                break;
            case BikeEditorAnalysisResult.Unavailable:
                LeverageRatioData = null;
                break;
            case BikeEditorAnalysisResult.Failed failed:
                LeverageRatioData = null;
                ErrorMessages.Add($"Linkage analysis failed: {failed.ErrorMessage}");
                break;
        }
    }

    private void ApplyImportedBike(ImportedBikeEditorData data)
    {
        bike = data.Bike;
        BaselineUpdated = 0;
        IsInDatabase = false;

        UpdateFromBike();
        ApplyAnalysisResult(data.AnalysisResult);
        EvaluateDirtiness();

        DeleteCommand.NotifyCanExecuteChanged();
        FakeDeleteCommand.NotifyCanExecuteChanged();
    }

    private void AttachJointPropertyChangedHandler(JointViewModel jointViewModel)
    {
        if (jointPropertyChangedHandlers.ContainsKey(jointViewModel)) return;

        PropertyChangedEventHandler handler = (_, pce) =>
        {
            switch (pce.PropertyName)
            {
                case nameof(jointViewModel.WasPossiblyDragged) when jointViewModel.WasPossiblyDragged:
                    jointViewModel.WasPossiblyDragged = false;
                    EvaluateDirtiness();
                    break;
                case nameof(jointViewModel.Name) or nameof(jointViewModel.Type):
                    EvaluateDirtiness();
                    break;
                case nameof(jointViewModel.X) when jointViewModel.Type is JointType.BottomBracket or JointType.RearWheel:
                case nameof(jointViewModel.Y) when jointViewModel.Type is JointType.BottomBracket or JointType.RearWheel:
                    UpdatePixelsToMillimeters();
                    break;
            }
        };

        jointPropertyChangedHandlers[jointViewModel] = handler;
        jointViewModel.PropertyChanged += handler;
    }

    private void DetachJointPropertyChangedHandler(JointViewModel jointViewModel)
    {
        if (!jointPropertyChangedHandlers.Remove(jointViewModel, out var handler)) return;

        jointViewModel.PropertyChanged -= handler;
    }

    private void DetachAllJointPropertyChangedHandlers()
    {
        foreach (var pair in jointPropertyChangedHandlers)
        {
            pair.Key.PropertyChanged -= pair.Value;
        }

        jointPropertyChangedHandlers.Clear();
    }

    private void AttachLinkPropertyChangedHandler(LinkViewModel linkViewModel)
    {
        if (linkPropertyChangedHandlers.ContainsKey(linkViewModel)) return;

        PropertyChangedEventHandler handler = (_, pce) =>
        {
            if (pce.PropertyName is nameof(linkViewModel.A) or nameof(linkViewModel.B))
            {
                EvaluateDirtiness();
            }
        };

        linkPropertyChangedHandlers[linkViewModel] = handler;
        linkViewModel.PropertyChanged += handler;
    }

    private void DetachLinkPropertyChangedHandler(LinkViewModel linkViewModel)
    {
        if (!linkPropertyChangedHandlers.Remove(linkViewModel, out var handler)) return;

        linkViewModel.PropertyChanged -= handler;
    }

    private void DetachAllLinkPropertyChangedHandlers()
    {
        foreach (var pair in linkPropertyChangedHandlers)
        {
            pair.Key.PropertyChanged -= pair.Value;
        }

        linkPropertyChangedHandlers.Clear();
    }

    private bool DidJointsChanged()
    {
        if (bike.Linkage is null || PixelsToMillimeters is null || Image is null) return false;

        var joints2 = JointViewModels.Select(jvm => jvm.ToJoint(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return bike.Linkage.Joints.Count != joints2.Count || !bike.Linkage.Joints.All(j => joints2.Contains(j));
    }

    private bool DidLinksChanged()
    {
        if (bike.Linkage is null || PixelsToMillimeters is null || Image is null) return false;

        var links2 = LinkViewModels
            .Where(lvm => lvm != shockViewModel && lvm.A is not null && lvm.B is not null)
            .Select(lvm => lvm.ToLink(Image.Size.Height, PixelsToMillimeters.Value)).ToList();
        return bike.Linkage.Links.Count != links2.Count || !bike.Linkage.Links.All(l => links2.Contains(l));
    }

    private void SetupJointsListeners()
    {
        JointViewModels.CollectionChanged += (_, e) =>
        {
            EvaluateDirtiness();

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllJointPropertyChangedHandlers();
                return;
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is JointViewModel jointViewModel)
                    {
                        DetachJointPropertyChangedHandler(jointViewModel);
                    }
                }
            }

            if (e.NewItems is null) return;
            foreach (var item in e.NewItems)
            {
                if (item is JointViewModel jointViewModel)
                {
                    AttachJointPropertyChangedHandler(jointViewModel);
                }
            }
        };
    }

    private void SetupLinksListeners()
    {
        LinkViewModels.CollectionChanged += (_, e) =>
        {
            EvaluateDirtiness();

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                DetachAllLinkPropertyChangedHandlers();
                return;
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is LinkViewModel linkViewModel)
                    {
                        DetachLinkPropertyChangedHandler(linkViewModel);
                    }
                }
            }

            if (e.NewItems is null) return;
            foreach (var item in e.NewItems)
            {
                if (item is LinkViewModel linkViewModel)
                {
                    AttachLinkPropertyChangedHandler(linkViewModel);
                }
            }
        };
    }

    #endregion Private methods

    #region TabPageViewModelBase overrides

    protected override void EvaluateDirtiness()
    {
        IsDirty =
            !IsInDatabase ||
            Name != bike.Name ||
            !MathUtils.AreEqual(HeadAngle, bike.HeadAngle) ||
            !MathUtils.AreEqual(ForksStroke, bike.ForkStroke) ||
            !MathUtils.AreEqual(ShockStroke, bike.ShockStroke) ||
            !MathUtils.AreEqual(Chainstay, bike.Chainstay) ||
            DidJointsChanged() ||
            DidLinksChanged();
    }

    protected override bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty &&
               HeadAngle is not null &&
               ForksStroke is not null &&
               (ShockStroke is null || (Image is not null && Chainstay is not null));
    }

    protected override async Task SaveImplementation()
    {
        if (bikeCoordinator is null) return;

        var newBike = ToBike();
        var result = await bikeCoordinator.SaveAsync(newBike, BaselineUpdated);

        switch (result)
        {
            case BikeSaveResult.Saved saved:
                bike = newBike;
                bike.Updated = saved.NewBaselineUpdated;
                BaselineUpdated = saved.NewBaselineUpdated;
                IsInDatabase = true;

                ApplyAnalysisResult(saved.AnalysisResult);
                EvaluateDirtiness();

                DeleteCommand.NotifyCanExecuteChanged();
                FakeDeleteCommand.NotifyCanExecuteChanged();

                Debug.Assert(App.Current is not null);
                if (!App.Current.IsDesktop)
                {
                    OpenPreviousPage();
                }
                break;

            case BikeSaveResult.Conflict conflict:
                var reload = await dialogService.ShowConfirmationAsync(
                    "Bike changed elsewhere",
                    "This bike has been updated from another source. Discard your changes and reload?");
                if (reload)
                {
                    bike = BikeFromSnapshot(conflict.CurrentSnapshot);
                    BaselineUpdated = conflict.CurrentSnapshot.Updated;
                    IsInDatabase = true;
                    UpdateFromBike();
                    QueuePlotRefresh();
                    EvaluateDirtiness();
                    DeleteCommand.NotifyCanExecuteChanged();
                    FakeDeleteCommand.NotifyCanExecuteChanged();
                }
                break;

            case BikeSaveResult.InvalidLinkage:
                ErrorMessages.Add("Linkage movement could not be calculated. Please check the joints and links!");
                break;

            case BikeSaveResult.Failed failed:
                ErrorMessages.Add($"Bike could not be saved: {failed.ErrorMessage}");
                break;
        }
    }

    private bool CanDelete() =>
        !IsInDatabase || dependencyQuery is null || !dependencyQuery.IsBikeInUse(Id);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(bool navigateBack)
    {
        if (bikeCoordinator is null) return;

        if (!IsInDatabase)
        {
            if (navigateBack) OpenPreviousPage();
            return;
        }

        var result = await bikeCoordinator.DeleteAsync(Id);
        switch (result.Outcome)
        {
            case BikeDeleteOutcome.Deleted:
                if (navigateBack) OpenPreviousPage();
                break;
            case BikeDeleteOutcome.InUse:
                ErrorMessages.Add("Bike is referenced by a setup and cannot be deleted.");
                break;
            case BikeDeleteOutcome.Failed:
                ErrorMessages.Add($"Bike could not be deleted: {result.ErrorMessage}");
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // Exists so the editor button strip can bind to a delete command.
    }

    protected override Task ResetImplementation()
    {
        UpdateFromBike();
        return RefreshAnalysisAsync(showPlotBusyOverlay: true);
    }

    protected override async Task ExportImplementation()
    {
        if (bikeCoordinator is null) return;

        var result = await bikeCoordinator.ExportBikeAsync(bike);
        if (result is BikeExportResult.Failed failed)
        {
            ErrorMessages.Add($"Bike could not be exported: {failed.ErrorMessage}");
        }
    }

    #endregion TabPageViewModelBase overrides

    #region Commands

    [RelayCommand]
    private void DoubleTapped(TappedEventArgs args)
    {
        var position = args.GetPosition(args.Source as Visual);
        JointViewModels.Add(new JointViewModel($"Point{pointNumber++}", JointType.Floating, position.X, position.Y, true));
    }

    [RelayCommand]
    private void Tapped()
    {
        SelectedLink = null;
        SelectedPoint = null;
        ClearSelections();
    }

    [RelayCommand]
    private void PointTapped(TappedEventArgs args)
    {
        if (args.Source is not Visual npv) return;

        ClearSelections();
        SelectedLink = null;
        SelectedPoint = npv.DataContext as JointViewModel;
        SelectedPoint!.IsSelected = true;

        // Prevent Tapped event on the Canvas, which would deselect the point.
        args.Handled = true;
    }

    [RelayCommand]
    private void LinkTapped(TappedEventArgs args)
    {
        if (args.Source is not Line lv) return;

        ClearSelections();
        SelectedPoint = null;

        SelectedLink = lv.DataContext as LinkViewModel;
        SelectedLink!.IsSelected = true;

        // Prevent Tapped event on the Canvas, which would deselect the link.
        args.Handled = true;
    }

    [RelayCommand]
    private void DeleteSelectedItem()
    {
        if (SelectedLink is not null && !SelectedLink.IsImmutable)
        {
            LinkViewModels.Remove(SelectedLink);
        }
        else if (SelectedPoint is not null && SelectedPoint.Immutability == Immutability.Modifiable)
        {
            var linksToDelete = LinkViewModels.Where(l => l.A == SelectedPoint || l.B == SelectedPoint).ToList();
            foreach (var link in linksToDelete)
            {
                LinkViewModels.Remove(link);
            }
            JointViewModels.Remove(SelectedPoint);
            ClearSelections();
        }
    }

    [RelayCommand]
    private async Task OpenImage()
    {
        if (bikeCoordinator is null) return;

        var workflowCts = StartReplaceableWorkflow();
        try
        {
            var result = await bikeCoordinator.LoadImageAsync(workflowCts.Token);
            if (!IsCurrentWorkflow(workflowCts)) return;

            switch (result)
            {
                case BikeImageLoadResult.Loaded loaded:
                    Image = loaded.Bitmap;
                    break;
                case BikeImageLoadResult.Failed failed:
                    ErrorMessages.Add($"Bike image could not be loaded: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException) when (workflowCts.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private void CreateLink()
    {
        var link = new LinkViewModel(null, null);
        link.UpdateLength(PixelsToMillimeters);
        LinkViewModels.Add(link);
    }

    [RelayCommand]
    private async Task Import()
    {
        if (bikeCoordinator is null) return;

        var workflowCts = StartReplaceableWorkflow();
        try
        {
            var result = await bikeCoordinator.ImportBikeAsync(workflowCts.Token);
            if (!IsCurrentWorkflow(workflowCts)) return;

            switch (result)
            {
                case BikeImportResult.Imported imported:
                    ApplyImportedBike(imported.Data);
                    break;
                case BikeImportResult.InvalidFile invalid:
                    ErrorMessages.Add(invalid.ErrorMessage);
                    break;
                case BikeImportResult.Failed failed:
                    ErrorMessages.Add($"Bike could not be imported: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException) when (workflowCts.IsCancellationRequested)
        {
        }
    }

    [RelayCommand]
    private async Task Loaded()
    {
        // Refresh Delete CanExecute when "in use" status changes.
        if (dependencyQuery is not null)
        {
            EnsureScopedSubscription(s => s.Add(dependencyQuery.Changes.Subscribe(_ =>
            {
                DeleteCommand.NotifyCanExecuteChanged();
                FakeDeleteCommand.NotifyCanExecuteChanged();
            })));
        }

        await RefreshAnalysisAsync();
    }

    [RelayCommand]
    private void Unloaded()
    {
        DisposeScopedSubscriptions();
        CancelReplaceableWorkflow();
    }

    #endregion Commands
}
