using Dalamud.Game;
using GatherBuddy.Enums;

namespace GatherBuddy.Classes;

public partial class GatheringNode
{
    public List<Gatherable> Items { get; init; }

    // Print all items separated by '|' or the given separator.
    public string PrintItems(string separator = "|", ClientLanguage lang = ClientLanguage.ChineseSimplified)
        => string.Join(separator, Items.Select(it => it.Name[lang]));

    // Node contains any of the given items (in english names).
    public bool HasItems(params Gatherable[] it)
        => it.Length == 0 || Items.Any(it.Contains);

    private void AddNodeToItem(Gatherable item)
    {
        item.NodeList.Add(this);
        if (item.NodeType == NodeType.无)
            item.NodeType = NodeType;
        else if (item.NodeType != NodeType.常规 && NodeType == NodeType.常规)
            item.NodeType = NodeType.常规;
        item.GatheringType = item.GatheringType.Add(GatheringType);
        item.ExpansionIdx  = Math.Min(item.ExpansionIdx, Territory.Data.ExVersion.RowId);
    }

    public bool AddItem(Gatherable item)
    {
        if (Items.Contains(item))
        {
            if (item.NodeList.Contains(this))
                return false;

            AddNodeToItem(item);
            return true;
        }

        Items.Add(item);
        AddNodeToItem(item);
        return true;
    }
}
