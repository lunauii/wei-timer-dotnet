using System;
using System.Collections.Generic;

namespace WeiTimer.Models;

/// <summary>Date-keyed daily carat totals. Keys are ISO date strings (yyyy-MM-dd).</summary>
public sealed class CaratLog
{
    public Dictionary<string, int> Totals { get; set; } = new();

    private static string TodayKey() => DateTime.Now.ToString("yyyy-MM-dd");

    public int Add(int amount)
    {
        var key = TodayKey();
        var newTotal = Totals.GetValueOrDefault(key, 0) + amount;
        Totals[key] = newTotal;
        return newTotal;
    }

    public int TodayTotal() => Totals.GetValueOrDefault(TodayKey(), 0);
}
