using Xunit;

namespace IntentEvalHarness.Tests;

public sealed class ToolIntentMappingTests
{
    [Theory]
    [InlineData("search_item", "SEARCH_ITEM")]
    [InlineData("add_item", "ADD_ITEM")]
    [InlineData("update_item", "UPDATE_ITEM")]
    [InlineData("delete_item", "DELETE_ITEM")]
    [InlineData("manage_box", "MANAGE_BOX")]
    [InlineData("view_inventory", "VIEW_INVENTORY")]
    [InlineData("upload_photo", "UPLOAD_PHOTO")]
    [InlineData("general_help", "GENERAL_HELP")]
    public void MapToolNameToIntent_ReturnsExpectedIntent(string toolName, string expectedIntent)
    {
        Assert.Equal(expectedIntent, NeedleIntentClient.MapToolNameToIntent(toolName));
    }

    [Fact]
    public void MapToolNameToIntent_UnknownToolReturnsNull()
    {
        Assert.Null(NeedleIntentClient.MapToolNameToIntent("unknown_tool"));
    }
}
