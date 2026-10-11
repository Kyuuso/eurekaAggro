using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace BFE.Windows;

/// <summary>
/// Base window providing automated positioning and coordinate reset capabilities.
/// Native Dalamud window implementation without external proprietary libraries.
/// </summary>
public abstract class PositionedWindow : Window
{
    private Vector2? pendingWindowPosition;
    private bool pendingPositionConditionReset;

    protected PositionedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        : base(name, flags)
    {
    }

    public void QueueResetToOrigin()
        => QueueWindowPosition(new Vector2(1f, 1f));

    public void QueueRandomVisibleJump()
        => QueueWindowPosition(GetRandomVisiblePosition());

    public override void PreDraw()
    {
        if (pendingWindowPosition.HasValue)
        {
            Position = pendingWindowPosition.Value;
            PositionCondition = ImGuiCond.Always;
            pendingWindowPosition = null;
            pendingPositionConditionReset = true;
        }
    }

    public override void PostDraw()
    {
    }

    protected void FinalizePendingWindowPlacement()
    {
        if (!pendingPositionConditionReset)
            return;

        pendingPositionConditionReset = false;
        Position = null;
        PositionCondition = ImGuiCond.None;
    }

    private void QueueWindowPosition(Vector2 position)
        => pendingWindowPosition = position;

    private Vector2 GetRandomVisiblePosition()
    {
        var viewport = ImGuiHelpers.MainViewport;
        var currentSize = Size ?? Vector2.Zero;
        var minimumSize = SizeConstraints?.MinimumSize ?? Vector2.Zero;
        var width = MathF.Max(currentSize.X, minimumSize.X);
        var height = MathF.Max(currentSize.Y, minimumSize.Y);
        var maxX = MathF.Max(1f, viewport.Size.X - width - 20f);
        var maxY = MathF.Max(1f, viewport.Size.Y - height - 20f);
        return new Vector2(1f + (Random.Shared.NextSingle() * maxX), 1f + (Random.Shared.NextSingle() * maxY));
    }
}
