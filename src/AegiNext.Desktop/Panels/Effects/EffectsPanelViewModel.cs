using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Avalonia.Media;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed class EffectsPanelViewModel : ObservableObject
{
    private string layerWidthText = "300";
    private string layerHeightText = "180";
    private string positionXText = "0";
    private string positionYText = "0";
    private string scaleXText = "1";
    private string scaleYText = "1";
    private string rotationText = "0";
    private string opacityText = "1";
    private string blurText = "0";
    private string keyframeValueText = "1";
    private string? validationError;
    private string? invalidFieldKey;
    private readonly WorkbenchSession session;
    private ProjectDocument document = new();
    private ProjectLayer? selectedLayer;
    private MediaTime position = MediaTime.Zero;
    private LayerListItem[] layers = [];
    private LayerListItem? selectedItem;
    private string layerStart = string.Empty;
    private string layerEnd = string.Empty;
    private decimal? layerWidth = 300;
    private decimal? layerHeight = 180;
    private bool canResizeLayer;
    private bool canEditPosition;
    private decimal? positionX = 0;
    private decimal? positionY = 0;
    private decimal? scaleX = 1;
    private decimal? scaleY = 1;
    private decimal? rotation = 0;
    private decimal? opacity = 1;
    private decimal? blur = 0;
    private int blend;
    private string[] blends = [];
    private bool? invertMask = false;
    private bool? orientPath = false;
    private AnimationPropertyChoice[] properties = [];
    private decimal? keyframeValue = 1;
    private decimal keyframeMinimum = -65504;
    private decimal keyframeMaximum = 65504;
    private int interpolation = 1;
    private string[] interpolations = [];
    private bool canAddKeyframe;
    private bool canDeleteKeyframe;
    private int preset = -1;
    private string[] presets = [];
    private string? presetName;

    internal EffectsPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        KeyframeColorDraft.Changed += (_, _) => OnPropertyChanged(nameof(KeyframeColorDraft));
        ResetPositionCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ResetPositionEffects)));
        AddPathPointCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.AddPathPoint)));
        RemovePathPointCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.RemovePathPoint)));
        ManageEffectScriptsCommand = new AsyncRelayCommand(() => session.RequestSettingsAsync(AegiNext.Desktop.Settings.SettingsPage.EFFECTS));
        PathCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.EditPath)));
        ClearPathCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ClearPath)));
        KeyframeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.AddKeyframe)));
        DeleteKeyframeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.DeleteKeyframe)));
        ApplyPresetCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ApplySelectedPreset)));
    }

    public string? ValidationError
    {
        get => validationError;
        internal set => SetProperty(ref validationError, value);
    }

    public string? InvalidFieldKey
    {
        get => invalidFieldKey;
        internal set => SetProperty(ref invalidFieldKey, value);
    }


    public ProjectDocument Document
    {
        get => document;
        set => SetProperty(ref document, value);
    }

    public ProjectLayer? SelectedLayer
    {
        get => selectedLayer;
        set
        {
            if (SetProperty(ref selectedLayer, value))
            {
                OnPropertyChanged(nameof(CanResetPosition));
            }
        }
    }

    public bool CanResetPosition => SelectedLayer?.SubtitleId is not null;

    public MediaTime Position
    {
        get => position;
        set => SetProperty(ref position, value);
    }

    public CanvasEditMode EditMode
    {
        get => session.SceneEditing.Mode;
        set
        {
            if (session.SceneEditing.Mode != value)
            {
                session.SceneEditing.Mode = value;
                OnPropertyChanged();
            }
        }
    }

    public LayerListItem[] Layers
    {
        get => layers;
        set => SetProperty(ref layers, value);
    }

    public LayerListItem? SelectedItem
    {
        get => selectedItem;
        set => SetProperty(ref selectedItem, value);
    }

    public Guid[] SelectedIds
    {
        get => session.SceneEditing.SelectedLayerIds;
        set
        {
            if (!session.SceneEditing.SelectedLayerIds.SequenceEqual(value))
            {
                session.SceneEditing.SelectedLayerIds = value;
                OnPropertyChanged();
            }
        }
    }

    public string LayerStart
    {
        get => layerStart;
        set => SetProperty(ref layerStart, value);
    }

    public string LayerEnd
    {
        get => layerEnd;
        set => SetProperty(ref layerEnd, value);
    }

    public decimal? LayerWidth
    {
        get => layerWidth;
        set => SetProperty(ref layerWidth, value);
    }

    public decimal? LayerHeight
    {
        get => layerHeight;
        set => SetProperty(ref layerHeight, value);
    }

    public bool CanResizeLayer
    {
        get => canResizeLayer;
        set => SetProperty(ref canResizeLayer, value);
    }

    public decimal? PositionX
    {
        get => positionX;
        set => SetProperty(ref positionX, value);
    }

    public bool CanEditPosition
    {
        get => canEditPosition;
        set => SetProperty(ref canEditPosition, value);
    }

    public decimal? PositionY
    {
        get => positionY;
        set => SetProperty(ref positionY, value);
    }

    public decimal? ScaleX
    {
        get => scaleX;
        set => SetProperty(ref scaleX, value);
    }

    public decimal? ScaleY
    {
        get => scaleY;
        set => SetProperty(ref scaleY, value);
    }

    public decimal? Rotation
    {
        get => rotation;
        set => SetProperty(ref rotation, value);
    }

    public decimal? Opacity
    {
        get => opacity;
        set => SetProperty(ref opacity, value);
    }

    public decimal? Blur
    {
        get => blur;
        set => SetProperty(ref blur, value);
    }

    public int Blend
    {
        get => blend;
        set => SetProperty(ref blend, value);
    }

    public string[] Blends
    {
        get => blends;
        set => SetProperty(ref blends, value);
    }

    public bool? InvertMask
    {
        get => invertMask;
        set => SetProperty(ref invertMask, value);
    }

    public bool? OrientPath
    {
        get => orientPath;
        set => SetProperty(ref orientPath, value);
    }

    private string editTargetLabel = string.Empty;

    public string EditTargetLabel
    {
        get => editTargetLabel;
        set => SetProperty(ref editTargetLabel, value);
    }

    public AnimationProperty Property
    {
        get => session.SceneEditing.Property;
        set
        {
            if (Property == value)
            {
                return;
            }
            if (!session.IsUpdating && !session.TryCommitDrafts())
            {
                OnPropertyChanged();
                return;
            }
            session.SceneEditing.Property = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProperty));
            OnPropertyChanged(nameof(IsVectorProperty));
            OnPropertyChanged(nameof(IsColorProperty));
            OnPropertyChanged(nameof(IsScalarProperty));
        }
    }

    public AnimationPropertyChoice[] Properties
    {
        get => properties;
        set
        {
            if (SetProperty(ref properties, value))
            {
                OnPropertyChanged(nameof(SelectedProperty));
            }
        }
    }

    public AnimationPropertyChoice? SelectedProperty
    {
        get => Properties.FirstOrDefault(choice => choice.Property == Property);
        set
        {
            if (value is not null)
            {
                Property = value.Property;
            }
        }
    }

    public ColorDraft KeyframeColorDraft { get; } = new();

    private decimal? keyframeValueY = 1;
    private string keyframeValueYText = "1";
    public decimal? KeyframeValueY { get => keyframeValueY; set => SetProperty(ref keyframeValueY, value); }
    public string KeyframeValueYText { get => keyframeValueYText; set => SetProperty(ref keyframeValueYText, value); }
    public bool IsVectorProperty => session.SceneEditing.Property is AnimationProperty.POSITION or AnimationProperty.SCALE;
    public bool IsColorProperty => session.SceneEditing.Property is AnimationProperty.FILL or AnimationProperty.STROKE;
    public bool IsScalarProperty => !IsVectorProperty && !IsColorProperty;

    public decimal? KeyframeValue
    {
        get => keyframeValue;
        set => SetProperty(ref keyframeValue, value);
    }

    public decimal KeyframeMinimum
    {
        get => keyframeMinimum;
        set => SetProperty(ref keyframeMinimum, value);
    }

    public decimal KeyframeMaximum
    {
        get => keyframeMaximum;
        set => SetProperty(ref keyframeMaximum, value);
    }

    public int Interpolation
    {
        get => interpolation;
        set => SetProperty(ref interpolation, value);
    }

    public string[] Interpolations
    {
        get => interpolations;
        set => SetProperty(ref interpolations, value);
    }

    public bool CanAddKeyframe
    {
        get => canAddKeyframe;
        set => SetProperty(ref canAddKeyframe, value);
    }

    public bool CanDeleteKeyframe
    {
        get => canDeleteKeyframe;
        set => SetProperty(ref canDeleteKeyframe, value);
    }

    public int Preset
    {
        get => preset;
        set => SetProperty(ref preset, value);
    }

    public string[] Presets
    {
        get => presets;
        set => SetProperty(ref presets, value);
    }

    public string? PresetName
    {
        get => presetName;
        set => SetProperty(ref presetName, value);
    }








    public ICommand ResetPositionCommand { get; }
    public ICommand AddPathPointCommand { get; }
    public ICommand RemovePathPointCommand { get; }
    public ICommand ManageEffectScriptsCommand { get; }


    public ICommand PathCommand { get; }


    public ICommand ClearPathCommand { get; }


    public ICommand KeyframeCommand { get; }

    public ICommand DeleteKeyframeCommand { get; }


    public ICommand ApplyPresetCommand { get; }




    /// <summary>同步图层及多选标识。</summary>
    public void SelectLayer(Guid id, Guid[] selectedIds) => session.SelectLayer(id, selectedIds);
    /// <summary>提交画布完成后的变换与路径参数。</summary>
    public Task CommitCanvasAsync(CanvasLayerEditEventArgs value) => session.CommitCanvasAsync(value);
    /// <summary>提交所有面板的有效草稿。</summary>
    public void CommitDrafts() => session.TryCommitDrafts(false);

    public void RestoreField(string fieldKey) => session.RestoreEffectDraftField(fieldKey);
    /// <summary>向工作台报告画布资源或工程渲染失败。</summary>
    public void ReportRenderingError(Exception error) => session.ShowError(error);
    public string LayerWidthText
    {
        get => layerWidthText;
        set => SetProperty(ref layerWidthText, value);
    }
    public string LayerHeightText
    {
        get => layerHeightText;
        set => SetProperty(ref layerHeightText, value);
    }
    public string PositionXText
    {
        get => positionXText;
        set => SetProperty(ref positionXText, value);
    }
    public string PositionYText
    {
        get => positionYText;
        set => SetProperty(ref positionYText, value);
    }
    public string ScaleXText
    {
        get => scaleXText;
        set => SetProperty(ref scaleXText, value);
    }
    public string ScaleYText
    {
        get => scaleYText;
        set => SetProperty(ref scaleYText, value);
    }
    public string RotationText
    {
        get => rotationText;
        set => SetProperty(ref rotationText, value);
    }
    public string OpacityText
    {
        get => opacityText;
        set => SetProperty(ref opacityText, value);
    }
    public string BlurText
    {
        get => blurText;
        set => SetProperty(ref blurText, value);
    }
    public string KeyframeValueText
    {
        get => keyframeValueText;
        set => SetProperty(ref keyframeValueText, value);
    }
    /// <summary>提交用户选择的混合模式。</summary>
    public void CommitBlend(int value)
    {
        Blend = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的关键帧插值。</summary>
    public void CommitInterpolation(int value)
    {
        Interpolation = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的反转蒙版状态。</summary>
    public void CommitInvertMask(bool value)
    {
        InvertMask = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的路径朝向状态。</summary>
    public void CommitOrientPath(bool value)
    {
        OrientPath = value;
        session.TryCommitDrafts();
    }
}
