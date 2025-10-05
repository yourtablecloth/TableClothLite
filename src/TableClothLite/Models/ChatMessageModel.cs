namespace TableClothLite.Models;

public sealed class ChatMessageModel
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
}
