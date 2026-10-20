namespace MoonBot.Core.Chat;

public enum ChatRole { System, User, Assistant }

public sealed record ChatMessage(ChatRole Role, string Text);
