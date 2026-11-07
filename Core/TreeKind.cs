namespace DynamicIslandNotifier.Core;

using System;
using System.Linq;

/// <summary>The kinds of tree you can plant.</summary>
public enum TreeKind { Oak, Pine, Cherry, Cactus, Orange, Bamboo, Maple, Willow, Wisteria, Firefly, Crystal, Bonsai }

/// <summary>Names and unlock goals of the trees.</summary>
public static class TreeCatalog
{
    public sealed record Info(TreeKind Kind, string Name, int UnlockHours);

    /// <summary>In the order shown; UnlockHours = hours of finished focus needed to use it.</summary>
    public static readonly Info[] All =
    {
        new(TreeKind.Oak,      "Sồi xanh",          0),
        new(TreeKind.Pine,     "Thông tuyết",       0),
        new(TreeKind.Cherry,   "Anh đào",           0),
        new(TreeKind.Cactus,   "Xương rồng nở hoa", 4),
        new(TreeKind.Orange,   "Cam vàng",          10),
        new(TreeKind.Bamboo,   "Trúc xanh",         20),
        new(TreeKind.Maple,    "Phong đỏ",          30),
        new(TreeKind.Willow,   "Liễu rủ",           50),
        new(TreeKind.Wisteria, "Tử đằng",           80),
        new(TreeKind.Firefly,  "Cây đom đóm",       120),
        new(TreeKind.Crystal,  "Cây pha lê",        180),
        new(TreeKind.Bonsai,   "Bonsai cổ thụ",     240),
    };

    public static Info Get(TreeKind kind) => All.First(i => i.Kind == kind);

    public static TreeKind Parse(string? value) =>
        Enum.TryParse<TreeKind>(value, ignoreCase: true, out var kind) ? kind : TreeKind.Oak;
}
