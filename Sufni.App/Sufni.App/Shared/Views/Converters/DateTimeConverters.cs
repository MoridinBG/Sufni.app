using System;
using Avalonia.Data.Converters;

namespace Sufni.App.Shared.Views.Converters;

public static class DateTimeConverters
{
    public static readonly FuncValueConverter<DateTime, DateTime> ToLocal = new(value => value.ToLocalTime());
}
