using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按稳定操作标识增加或更新有序变换；数组位置决定求值顺序，拒绝覆盖关键帧轨道。</summary>
    public void SetAnimationTransform(Guid layerId, AnimationTrackTarget target, AnimationValue initialValue,
        AnimationTransformOperation operation, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        UpdateLayer(layerId, layer =>
        {
            var existing = layer.Tracks.FirstOrDefault(track => track.Target == target);
            if (existing is not null && !existing.IsOrdered)
            {
                throw new InvalidOperationException("关键帧轨道必须先显式清除，不能隐式替换为有序变换。");
            }

            var operations = (existing?.Transforms ?? []).ToBuilder();
            var oldIndex = FindOperationIndex(operations, operation.Id);
            if (oldIndex >= 0)
            {
                operations[oldIndex] = operation;
            }
            else
            {
                operations.Add(operation);
                oldIndex = operations.Count - 1;
            }

            if (index is { } destination)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(destination);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destination, operations.Count);
                operations.RemoveAt(oldIndex);
                operations.Insert(destination, operation);
            }

            var transforms = operations.ToImmutable();
            if (existing?.InitialValue == initialValue && transforms.SequenceEqual(existing.Transforms))
            {
                return layer;
            }

            var next = new AnimationTrack(target, []) { InitialValue = initialValue, Transforms = transforms };
            return layer with { Tracks = layer.Tracks.Where(track => track.Target != target).Append(next).ToImmutableArray() };
        });
    }

    /// <summary>按稳定标识删除有序变换；删除最后操作同时移除该轨道。</summary>
    public void RemoveAnimationTransform(Guid layerId, AnimationTrackTarget target, Guid operationId)
    {
        UpdateLayer(layerId, layer =>
        {
            var track = GetOrderedTrack(layer, target);
            var index = FindOperationIndex(track.Transforms.ToBuilder(), operationId);
            if (index < 0)
            {
                throw new KeyNotFoundException("有序变换操作不存在。");
            }

            var remaining = track.Transforms.RemoveAt(index);
            var tracks = layer.Tracks.Where(value => value.Target != target);
            return layer with { Tracks = (remaining.IsEmpty ? tracks : tracks.Append(track with { Transforms = remaining })).ToImmutableArray() };
        });
    }

    /// <summary>按稳定标识修改有序变换的数组顺序，不改变时间、指数或目标值。</summary>
    public void MoveAnimationTransform(Guid layerId, AnimationTrackTarget target, Guid operationId, int index)
    {
        UpdateLayer(layerId, layer =>
        {
            var track = GetOrderedTrack(layer, target);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, track.Transforms.Length);
            var operations = track.Transforms.ToBuilder();
            var oldIndex = FindOperationIndex(operations, operationId);
            if (oldIndex < 0)
            {
                throw new KeyNotFoundException("有序变换操作不存在。");
            }

            if (oldIndex == index)
            {
                return layer;
            }

            var operation = operations[oldIndex];
            operations.RemoveAt(oldIndex);
            operations.Insert(index, operation);
            var next = track with { Transforms = operations.ToImmutable() };
            return layer with { Tracks = layer.Tracks.Select(value => value.Target == target ? next : value).ToImmutableArray() };
        });
    }

    private static AnimationTrack GetOrderedTrack(ProjectLayer layer, AnimationTrackTarget target)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == target) ?? throw new KeyNotFoundException("动画目标不存在。");
        return track.IsOrdered ? track : throw new InvalidOperationException("目标轨道使用普通关键帧。");
    }

    private static int FindOperationIndex(ImmutableArray<AnimationTransformOperation>.Builder operations, Guid id)
    {
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
