namespace COMTool.App.Services;

/// <summary>
/// r67：标签拖动换位的目标索引计算（纯函数，可单测）。
/// 拖动时指针 X 越过某标签中线即插入其前；全部越过则排到末尾。
/// </summary>
public static class TabReorderIndex
{
    /// <param name="pointerX">指针在标签条坐标系中的 X。</param>
    /// <param name="tabCenters">其余（不含被拖标签）各标签的水平中心 X，按当前显示顺序。</param>
    /// <returns>被拖标签在完整列表中的目标索引（0..Count-1 语义：剩余列表的插入点即完整列表的最终索引）。</returns>
    public static int Resolve(double pointerX, IReadOnlyList<double> tabCenters)
    {
        for (var i = 0; i < tabCenters.Count; i++)
        {
            if (pointerX < tabCenters[i])
            {
                return i;
            }
        }

        return tabCenters.Count;
    }
}
