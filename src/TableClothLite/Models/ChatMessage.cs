﻿namespace TableClothLite.Models;

public sealed class ChatMessage
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
}
