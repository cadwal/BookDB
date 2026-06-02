using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace BookDB.Desktop.Tests;

/// <summary>
/// ColumnState record represents persisted column configuration for the book list DataGrid.
/// Verifies the JSON serialisation contract used to persist and restore column layout.
/// </summary>
public record ColumnState(string Name, bool IsVisible, int DisplayIndex, double Width);

public class ColumnStateTests
{
    [Fact]
    public void ColumnState_SerializeDeserialize_RoundTrips()
    {
        var states = new List<ColumnState>
        {
            new ColumnState("Title",    IsVisible: true,  DisplayIndex: 0, Width: 200.0),
            new ColumnState("Author",   IsVisible: true,  DisplayIndex: 1, Width: 150.0),
            new ColumnState("Series",   IsVisible: false, DisplayIndex: 2, Width: 100.0),
            new ColumnState("Publisher",IsVisible: true,  DisplayIndex: 3, Width: 120.0),
        };

        var json = JsonSerializer.Serialize(states);
        var deserialized = JsonSerializer.Deserialize<List<ColumnState>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(states.Count, deserialized!.Count);

        for (int i = 0; i < states.Count; i++)
        {
            Assert.Equal(states[i].Name,         deserialized[i].Name);
            Assert.Equal(states[i].IsVisible,     deserialized[i].IsVisible);
            Assert.Equal(states[i].DisplayIndex,  deserialized[i].DisplayIndex);
            Assert.Equal(states[i].Width,         deserialized[i].Width);
        }
    }
}
