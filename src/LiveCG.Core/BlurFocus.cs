namespace LiveCG.Core;

public sealed record BlurFocusStep(Guid RegionId, StrengthLevel Strength = StrengthLevel.Medium)
{
    public double Scale => Strength switch
    {
        StrengthLevel.VeryWeak => 1.04, StrengthLevel.Weak => 1.08,
        StrengthLevel.Medium => 1.15, StrengthLevel.Strong => 1.24, _ => 1.35
    };
}

public sealed record BlurFocus
{
    public bool WholeFrame { get; init; }
    public Guid BackgroundId { get; init; }
    public double BlurPixels { get; set; } = 12;
    public bool PlayTimelineBeforeFocus { get; init; }
    public double Seconds { get; set; } = .45;
    public List<BlurFocusStep> Steps { get; init; } = [];

    public BlurFocus Copy() => this with { Steps = [.. Steps] };

    public void Validate()
    {
        if ((!WholeFrame && BackgroundId == Guid.Empty) || !double.IsFinite(BlurPixels) || BlurPixels is < 0 or > 64 ||
            !double.IsFinite(Seconds) || Seconds is < .1 or > 5 || Steps is null || Steps.Count > 100 ||
            Steps.Any(step => step is null || step.RegionId == BackgroundId || !Enum.IsDefined(step.Strength)) ||
            Steps.Select(step => step.RegionId).Distinct().Count() != Steps.Count)
            throw new InvalidDataException("순차 블러 강조 설정을 확인하세요.");
    }

    public static bool Inside(NormalizedRect inner, NormalizedRect outer) =>
        inner.X >= outer.X - 1e-7 && inner.Y >= outer.Y - 1e-7 &&
        inner.X + inner.Width <= outer.X + outer.Width + 1e-7 &&
        inner.Y + inner.Height <= outer.Y + outer.Height + 1e-7;

    public BlurFocus? ForOutput(IReadOnlyList<EffectRegion> regions)
    {
        var background = regions.FirstOrDefault(region => region.Id == BackgroundId && LayerGraph.Visible(region, regions));
        if (background is null && !WholeFrame) return null;
        var available = regions.Where(region => LayerGraph.Visible(region, regions) && (WholeFrame || Inside(region.Bounds, background!.Bounds)))
            .Select(region => region.Id).ToHashSet();
        var steps = Steps.Where(step => available.Contains(step.RegionId)).ToList();
        return steps.Count == 0 ? null : this with { Steps = steps };
    }
}
