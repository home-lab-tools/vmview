using Avalonia;
using Avalonia.Animation;

namespace VmView.Controls;

/// <summary>
/// A horizontal page slide whose direction is ours to set: TransitioningContentControl always asks for
/// "forward", but going back up the stack should slide the other way, as on a phone.
/// </summary>
public sealed class SlideTransition : IPageTransition
{
    readonly PageSlide _slide = new(TimeSpan.FromMilliseconds(240), PageSlide.SlideAxis.Horizontal);

    /// <summary>True = the new page comes in from the right (deeper); false = from the left (back).</summary>
    public bool Forward { get; set; } = true;

    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
        => _slide.Start(from, to, Forward, cancellationToken);
}
