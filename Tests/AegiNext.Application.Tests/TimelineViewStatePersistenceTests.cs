using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineViewStatePersistenceTests
{
    [Fact]
    public async Task FileRoundTripPreservesIndependentMixedTrackAnimationRowsByOwnerAndProperty()
    {
        using var directory = new TemporaryProjectDirectory();
        var firstTrack = new ProjectTrack { Name = "First" };
        var secondTrack = new ProjectTrack { Name = "Second" };
        var firstScene = new ProjectLayer
        {
            Id = firstTrack.Id, TrackId = firstTrack.Id,
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(2), End = new(4),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
        };
        var secondScene = new ProjectLayer
        {
            TrackId = secondTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 20, 20), Start = new(2), End = new(4),
            Tracks = [new(AnimationProperty.FILL, [new(new(0), SceneColor.White)])]
        };
        var editor = new ProjectEditor(new() { Tracks = [firstTrack, secondTrack] });
        var firstCue = editor.AddSubtitle(new(0), new(2), "First", firstTrack.Id);
        var secondCue = editor.AddSubtitle(new(0), new(2), "Second", secondTrack.Id);
        editor.SetKeyframe(firstCue, AnimationProperty.OPACITY, new(new(0), 0.5));
        editor.SetKeyframe(secondCue, AnimationProperty.POSITION, new(new(0), new ScenePoint(10, 20)));
        var state = new TimelineViewState
        {
            CollapsedAnimationRows =
            [
                new(TimelineRowScope.TRACK, firstTrack.Id, AnimationProperty.OPACITY),
                new(TimelineRowScope.TRACK, secondTrack.Id, AnimationProperty.POSITION),
                new(TimelineRowScope.TRACK, firstTrack.Id, AnimationProperty.FILL),
                new(TimelineRowScope.TRACK, secondTrack.Id, AnimationProperty.FILL)
            ]
        };
        var document = editor.Snapshot with
        {
            Layers = [.. editor.Snapshot.Layers, firstScene, secondScene],
            TimelineViewState = state
        };
        var path = Path.Combine(directory.Path, "timeline.aeginext");

        await ProjectStore.SaveAsync(document, path);
        var loaded = await ProjectStore.LoadAsync(path);

        Assert.Equal(state.CollapsedAnimationRows.ToArray(), loaded.TimelineViewState.CollapsedAnimationRows.ToArray());
        Assert.DoesNotContain(new TimelineAnimationRowId(TimelineRowScope.TRACK, firstTrack.Id,
            AnimationProperty.POSITION), loaded.TimelineViewState.CollapsedAnimationRows);
        Assert.DoesNotContain(new TimelineAnimationRowId(TimelineRowScope.TRACK, secondTrack.Id,
            AnimationProperty.OPACITY), loaded.TimelineViewState.CollapsedAnimationRows);
        Assert.Equal(ProjectStore.Serialize(document), ProjectStore.Serialize(loaded));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void CurrentVersionWithoutTimelineViewStateDefaultsToExpandedRows()
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()))!.AsObject();
        Assert.True(root.Remove("timelineViewState"));
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());

        var loaded = ProjectStore.Deserialize(bytes);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, loaded.Version);
        Assert.NotNull(loaded.TimelineViewState);
        Assert.False(loaded.TimelineViewState.CollapsedAnimationRows.IsDefault);
        Assert.Empty(loaded.TimelineViewState.CollapsedAnimationRows);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyFixturesWithoutTimelineViewStateMigrateToExpandedRows(int version)
    {
        var root = LegacyProjectJsonFixture.Create(version);
        Assert.True(root.Remove("timelineViewState"));
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());

        var loaded = ProjectStore.Deserialize(bytes);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, loaded.Version);
        Assert.NotNull(loaded.TimelineViewState);
        Assert.False(loaded.TimelineViewState.CollapsedAnimationRows.IsDefault);
        Assert.Empty(loaded.TimelineViewState.CollapsedAnimationRows);
        Assert.Equal("legacy", Assert.Single(loaded.Subtitles).Text);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(Assert.Single(loaded.Layers).Tracks).Property);
    }

    [Fact]
    public async Task DormantOwnersAndTemporarilyAbsentAnimationPropertiesRemainSaveable()
    {
        using var directory = new TemporaryProjectDirectory();
        var absentOwner = Guid.NewGuid();
        var document = new ProjectDocument
        {
            TimelineViewState = new()
            {
                CollapsedAnimationRows =
                [
                    new(TimelineRowScope.TRACK, absentOwner, AnimationProperty.OPACITY),
                    new(TimelineRowScope.TRACK, absentOwner, AnimationProperty.MASK_NODE_POSITION),
                    new(TimelineRowScope.TRACK, ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.POSITION)
                ]
            }
        };
        var path = Path.Combine(directory.Path, "dormant.aeginext");

        ProjectValidator.Validate(document);
        await ProjectStore.SaveAsync(document, path);
        var loaded = await ProjectStore.LoadAsync(path);

        Assert.Empty(loaded.Layers);
        Assert.Empty(loaded.Subtitles);
        Assert.Equal(document.TimelineViewState.CollapsedAnimationRows.ToArray(),
            loaded.TimelineViewState.CollapsedAnimationRows.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void InvalidViewStateIsRejectedByValidatorAndSerializer(int mutation)
    {
        var row = new TimelineAnimationRowId(TimelineRowScope.TRACK,
            ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY);
        var state = new TimelineViewState { CollapsedAnimationRows = [row] };
        state = mutation switch
        {
            0 => state with { CollapsedAnimationRows = [row, row with { }] },
            1 => state with { CollapsedAnimationRows = [row with { OwnerId = Guid.Empty }] },
            2 => state with { CollapsedAnimationRows = [row with { Scope = (TimelineRowScope)int.MaxValue }] },
            3 => state with { CollapsedAnimationRows = [row with { Property = AnimationProperty.POSITION_X }] },
            4 => state with { CollapsedAnimationRows = [row with { Property = (AnimationProperty)int.MaxValue }] },
            5 => state with { CollapsedAnimationRows = [null!] },
            6 => state with { CollapsedAnimationRows = default },
            7 => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var document = new ProjectDocument { TimelineViewState = state };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void InvalidSerializedViewStateCannotBypassDocumentValidation(int mutation)
    {
        var document = new ProjectDocument
        {
            TimelineViewState = new()
            {
                CollapsedAnimationRows =
                [new(TimelineRowScope.TRACK, ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY)]
            }
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        var state = root["timelineViewState"]!.AsObject();
        var rows = state["collapsedAnimationRows"]!.AsArray();
        var row = rows[0]!.AsObject();
        switch (mutation)
        {
            case 0:
                rows.Add(row.DeepClone());
                break;
            case 1:
                row["ownerId"] = Guid.Empty;
                break;
            case 2:
                row["scope"] = "UNKNOWN_SCOPE";
                break;
            case 3:
                row["property"] = "POSITION_X";
                break;
            case 4:
                row["property"] = "UNKNOWN_PROPERTY";
                break;
            case 5:
                rows[0] = null;
                break;
            case 6:
                state["collapsedAnimationRows"] = null;
                break;
            case 7:
                root["timelineViewState"] = null;
                break;
            case 8:
                Assert.True(state.Remove("collapsedAnimationRows"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
