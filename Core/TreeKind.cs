namespace DynamicIslandNotifier.Core;

using System;
using System.Linq;

/// <summary>The kinds of tree you can plant.</summary>
public enum TreeKind { Oak, Pine, Cherry, Cactus, Orange, Bamboo, Maple, Willow, Wisteria, Firefly, Crystal, Bonsai }

/// <summary>Names and unlock goals of the trees.</summary>
public static class TreeCatalog
{
    public sealed record Info(TreeKind Kind, string Name, int UnlockAt);

    /// <summary>In the order shown; UnlockAt = grown trees needed to use it.</summary>
    public static readonly Info[] All =
    {
        new(TreeKind.Oak,      "Sồi xanh",          0),
        new(TreeKind.Pine,     "Thông tuyết",       0),
        new(TreeKind.Cherry,   "Anh đào",           0),
        new(TreeKind.Cactus,   "Xương rồng nở hoa", 3),
        new(TreeKind.Orange,   "Cam vàng",          6),
        new(TreeKind.Bamboo,   "Trúc xanh",         10),
        new(TreeKind.Maple,    "Phong đỏ",          15),
        new(TreeKind.Willow,   "Liễu rủ",           20),
        new(TreeKind.Wisteria, "Tử đằng",           30),
        new(TreeKind.Firefly,  "Cây đom đóm",       40),
        new(TreeKind.Crystal,  "Cây pha lê",        50),
        new(TreeKind.Bonsai,   "Bonsai cổ thụ",     70),
    };

    public static Info Get(TreeKind kind) => All.First(i => i.Kind == kind);

    public static TreeKind Parse(string? value) =>
        Enum.TryParse<TreeKind>(value, ignoreCase: true, out var kind) ? kind : TreeKind.Oak;
}
