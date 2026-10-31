namespace DynamicIslandNotifier.Core;

using System;
using System.Linq;

/// <summary>The kinds of tree you can plant.</summary>
public enum TreeKind { Oak, Pine, Cherry, Cactus, Orange, Bamboo }

/// <summary>Names and unlock goals of the trees.</summary>
public static class TreeCatalog
{
    public sealed record Info(TreeKind Kind, string Name, int UnlockAt);

    /// <summary>In the order shown; UnlockAt = grown trees needed to use it.</summary>
    public static readonly Info[] All =
    {
        new(TreeKind.Oak,    "Cây xanh",   0),
        new(TreeKind.Pine,   "Thông",      0),
        new(TreeKind.Cherry, "Anh đào",    0),
        new(TreeKind.Cactus, "Xương rồng", 3),
        new(TreeKind.Orange, "Cây cam",    6),
        new(TreeKind.Bamboo, "Tre",        10),
    };

    public static Info Get(TreeKind kind) => All.First(i => i.Kind == kind);

    public static TreeKind Parse(string? value) =>
        Enum.TryParse<TreeKind>(value, ignoreCase: true, out var kind) ? kind : TreeKind.Oak;
}
