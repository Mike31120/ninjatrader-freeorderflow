# My Order Flow Custom indicators for NinjaTrader 8

See releases pages for downloads

## Build

To compile the custom indicators run:

```
dotnet build -c Release MyOrderFlowCustom.sln
```

The resulting `MyOrderFlowCustom.dll` will be available in `bin/Release`.

## Global HVN/LVN levels

`MofRangeVolumeProfile` exposes the detected HVN and LVN levels through the
static dictionaries `GlobalHvnLevels` and `GlobalLvnLevels`. An example
indicator `MofGlobalLevelLines` reads these lists at each `OnBarUpdate` and
draws global horizontal lines using `Draw.HorizontalLine`.

To display the levels:

1. Add a *Fixed Range Volume Profile* drawing on your chart.
2. Enable **Use Global Levels** in its properties so the levels are exported.
3. Add the `MOF Global Level Lines` indicator to the same instrument. It will
   automatically create and remove the global lines as new levels are detected.
   You can now configure the HVN and LVN line styles and toggle their
   visibility from the indicator properties.
