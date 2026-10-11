using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Dashboard;

public static class DashboardEditor
{
    public static int IndexOf(List<CardSettings> cards, string metricId) => cards.FindIndex(c => c.MetricId == metricId);

    public static bool Add(List<CardSettings> cards, string metricId)
    {
        if (IndexOf(cards, metricId) >= 0)
        {
            return false;
        }

        cards.Add(new CardSettings { MetricId = metricId });
        return true;
    }

    public static bool Remove(List<CardSettings> cards, string metricId)
    {
        var index = IndexOf(cards, metricId);
        if (index < 0)
        {
            return false;
        }

        cards.RemoveAt(index);
        return true;
    }

    public static int MoveTo(List<CardSettings> cards, string metricId, int newIndex)
    {
        var index = IndexOf(cards, metricId);
        if (index < 0)
        {
            return -1;
        }

        newIndex = Math.Clamp(newIndex, 0, cards.Count - 1);
        var card = cards[index];
        cards.RemoveAt(index);
        cards.Insert(newIndex, card);
        return newIndex;
    }

    public static int MoveBy(List<CardSettings> cards, string metricId, int delta)
    {
        var index = IndexOf(cards, metricId);
        return index < 0 ? -1 : MoveTo(cards, metricId, index + delta);
    }
}
