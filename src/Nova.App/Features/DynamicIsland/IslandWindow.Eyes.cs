using System.Windows;
using Nova.Core.Configuration;

namespace Nova.App.Features.DynamicIsland;

/// <summary>
/// The eye catalogue of the compact Island. Every style except Pet is a <see cref="RealEye"/>: the same almond-shaped anime eye with moving
/// lids, lashes and a gaze, and only the iris differs (Sharingan tomoe, Rinnegan rings, a flickering ghoul eye, a slit cat pupil, lightning for
/// Zone and ShadowMonarch, ...). Their looks and animations live in RealEye.cs; what the eyes do (look, blink, vanish, wink) in
/// IslandWindow.EyeBrain.cs. Pet keeps its own simple round eyes (see PetEye).
/// </summary>
public partial class IslandWindow
{
    /// <summary>One eye of the given style; <paramref name="idx"/> is 0 (left) or 1 (right), because some characters have two different eyes.</summary>
    FrameworkElement MakeEye(IslandFaceStyle style, double h, int idx)
        => new RealEye(RealLook(style, idx) ?? EyeLooks.AnimeAura, h, idx);
}
